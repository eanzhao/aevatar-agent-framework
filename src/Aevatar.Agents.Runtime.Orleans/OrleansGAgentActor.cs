using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Microsoft.Extensions.Logging;
using Orleans.Streams;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Orleans Agent Actor
/// 继承自 GAgentActorBase,使用 Orleans Streams 进行事件传输
/// </summary>
public class OrleansGAgentActor : GAgentActorBase
{
    private readonly IGrainFactory _grainFactory;
    private readonly IStreamProvider _streamProvider;
    private readonly Dictionary<Guid, IAsyncStream<EventEnvelope>> _actorStreams = new();
    private IAsyncStream<EventEnvelope>? _myStream;
    private StreamSubscriptionHandle<EventEnvelope>? _streamSubscription;

    // ============ 构造函数 ============

    /// <summary>
    /// 主构造函数 - 用于新的实现
    /// </summary>
    public OrleansGAgentActor(
        IGAgent agent,
        IGrainFactory grainFactory,
        IStreamProvider streamProvider,
        ILogger? logger = null)
        : base(agent, logger)
    {
        _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
        _streamProvider = streamProvider;

        // 创建自己的 Stream
        try
        {
            var streamId = StreamId.Create(AevatarAgentsOrleansConstants.StreamNamespace, Id.ToString());
            _myStream = _streamProvider.GetStream<EventEnvelope>(streamId);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to create Orleans stream for Agent {AgentId}", Id);
            _myStream = null;
        }
    }

    /// <summary>
    /// 获取内部的 Grain 引用（兼容性支持）
    /// </summary>
    public IGAgentGrain? GetGrain() => null;

    // ============ 抽象方法实现 ============

    /// <summary>
    /// 发送事件给自己 - 通过 Orleans Stream
    /// </summary>
    protected override async Task SendToSelfAsync(EventEnvelope envelope, CancellationToken ct)
    {
        if (_myStream != null)
        {
            await _myStream.OnNextAsync(envelope);
        }
        else
        {
            // Fallback: 直接调用处理
            await HandleEventAsync(envelope, ct);
        }
    }

    /// <summary>
    /// 发送事件到指定 Actor - 通过 Orleans Streams
    /// </summary>
    protected override async Task SendEventToActorAsync(Guid actorId, EventEnvelope envelope, CancellationToken ct)
    {
        try
        {
            // 获取或创建目标 Actor 的 Stream
            if (!_actorStreams.TryGetValue(actorId, out var stream))
            {
                var streamId = StreamId.Create(AevatarAgentsOrleansConstants.StreamNamespace, actorId.ToString());
                stream = _streamProvider.GetStream<EventEnvelope>(streamId);
                _actorStreams[actorId] = stream;
            }

            await stream.OnNextAsync(envelope);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to send event {EventId} to Actor {ActorId}", envelope.Id, actorId);

            // Fallback: 直接通过 Grain 调用
            try
            {
                var grain = _grainFactory.GetGrain<IGAgentGrain>(actorId.ToString());
                using var stream = new MemoryStream();
                using var codedOutput = new Google.Protobuf.CodedOutputStream(stream);
                envelope.WriteTo(codedOutput);
                codedOutput.Flush();
                await grain.HandleEventAsync(stream.ToArray());
            }
            catch (Exception fallbackEx)
            {
                Logger.LogError(fallbackEx, "Fallback to Grain call also failed for Actor {ActorId}", actorId);
                throw;
            }
        }
    }

    /// <summary>
    /// 激活 Actor - 订阅 Orleans Stream
    /// </summary>
    public override async Task ActivateAsync(CancellationToken ct = default)
    {
        Logger.LogInformation("Activating Orleans Actor {ActorId}", Id);

        // 订阅自己的 Stream
        if (_myStream != null)
        {
            try
            {
                _streamSubscription = await _myStream.SubscribeAsync(OnStreamEventReceived);
                Logger.LogDebug("Successfully subscribed to Orleans stream for Agent {AgentId}", Id);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to subscribe to stream for Agent {AgentId}", Id);
            }
        }

        // 如果 Agent 支持事件溯源,触发事件回放 (在 Actor 层触发,而不是 Agent 层)
        // Check if Agent inherits from GAgentBaseWithEventSourcing<TState>
        var agentType = Agent.GetType();
        var baseType = agentType.BaseType;
        while (baseType != null && baseType != typeof(object))
        {
            if (baseType.IsGenericType &&
                baseType.GetGenericTypeDefinition().Name == "GAgentBaseWithEventSourcing`1")
            {
                // Found GAgentBaseWithEventSourcing<TState>
                // Use reflection to get and call ReplayEventsAsync
                var replayMethod = baseType.GetMethod("ReplayEventsAsync", new[] { typeof(CancellationToken) });
                if (replayMethod != null)
                {
                    Logger.LogInformation("Agent {AgentId} supports event sourcing, triggering replay", Id);
                    try
                    {
                        var task = (Task)replayMethod.Invoke(Agent, new object[] { ct })!;
                        await task;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Error replaying events for Agent {AgentId}", Id);
                    }
                }
                break;
            }
            baseType = baseType.BaseType;
        }

        Logger.LogInformation("Orleans Actor {ActorId} activated successfully", Id);
    }

    /// <summary>
    /// 停用 Actor - 取消订阅并清理资源
    /// </summary>
    public override async Task DeactivateAsync(CancellationToken ct = default)
    {
        Logger.LogInformation("Deactivating Orleans Actor {ActorId}", Id);

        // 取消 Stream 订阅
        if (_streamSubscription != null)
        {
            try
            {
                await _streamSubscription.UnsubscribeAsync();
                Logger.LogDebug("Successfully unsubscribed from Orleans stream for Agent {AgentId}", Id);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to unsubscribe from stream for Agent {AgentId}", Id);
            }
        }

        // 清空 Stream 缓存
        _actorStreams.Clear();

        Logger.LogInformation("Orleans Actor {ActorId} deactivated", Id);
    }

    // ============ Stream 事件处理 ============

    /// <summary>
    /// 处理从 Orleans Stream 接收到的事件
    /// </summary>
    private async Task OnStreamEventReceived(EventEnvelope envelope, StreamSequenceToken? token)
    {
        Logger.LogDebug("Agent {AgentId} received event {EventId} from stream", Id, envelope.Id);

        try
        {
            // 直接调用基类的 HandleEventAsync (会走完整的处理流程)
            await HandleEventAsync(envelope, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error handling stream event {EventId} for Agent {AgentId}", envelope.Id, Id);
        }
    }
}
