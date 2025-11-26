using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Streams;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Orleans Agent Actor
/// 继承自 GAgentActorBase,使用 IMessageStream (Orleans Streams 或 MassTransit) 进行事件传输
/// </summary>
public class OrleansGAgentActor : GAgentActorBase
{
    private readonly IGrainFactory _grainFactory;
    
    // Orleans Stream Specifics
    private readonly IStreamProvider _orleansStreamProvider;
    private readonly StreamingOptions _streamingOptions;
    
    // Abstracted Stream
    private readonly IMessageStreamProvider? _externalStreamProvider;
    private readonly MessageStreamProviderOptions _providerOptions;
    
    // Cache for other actors' streams
    private readonly Dictionary<Guid, IMessageStream> _actorStreams = new();
    
    // My Stream
    private IMessageStream? _myStream;
    private IMessageStreamSubscription? _streamSubscription;

    // ============ 构造函数 ============

    /// <summary>
    /// 主构造函数
    /// </summary>
    public OrleansGAgentActor(
        IGAgent agent,
        IGrainFactory grainFactory,
        IStreamProvider orleansStreamProvider,
        StreamingOptions streamingOptions,
        ILogger<OrleansGAgentActor> logger,
        IMessageStreamProvider? externalStreamProvider = null,
        IOptions<MessageStreamProviderOptions>? providerOptions = null)
        : base(agent)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
        _orleansStreamProvider = orleansStreamProvider;
        _streamingOptions = streamingOptions ?? new StreamingOptions();
        _externalStreamProvider = externalStreamProvider;
        _providerOptions = providerOptions?.Value ?? new MessageStreamProviderOptions();

        InitializeStream();
    }

    private void InitializeStream()
    {
        try
        {
            // Determine which provider to use
            // Priority: Configuration > Default (Orleans)
            var providerType = _providerOptions.Provider;
            
            Logger.LogWarning("DEBUG: [OrleansGAgentActor] Initializing stream. Default Provider: {Provider}", providerType);

            // Check runtime-specific override
            if (_providerOptions.Runtime.TryGetValue("Orleans", out var runtimeProvider))
            {
                providerType = runtimeProvider;
                Logger.LogWarning("DEBUG: [OrleansGAgentActor] Runtime 'Orleans' override: {Provider}", providerType);
            }

            Logger.LogWarning("DEBUG: [OrleansGAgentActor] Final ProviderType: {ProviderType}, ExternalProvider: {HasExternalProvider}", 
                providerType, _externalStreamProvider != null);

            if (providerType == "MassTransit" && _externalStreamProvider != null)
            {
                // Use External Provider (MassTransit)
                _myStream = _externalStreamProvider.GetStream(Id);
                Logger.LogWarning("DEBUG: Agent {AgentId} using MassTransit stream", Id);
            }
            else
            {
                // Use Orleans Stream (Default)
                var streamNamespace = _streamingOptions.DefaultStreamNamespace ?? AevatarAgentsOrleansConstants.StreamNamespace;
                var streamId = StreamId.Create(streamNamespace, Id.ToString());
                var orleansStream = _orleansStreamProvider.GetStream<byte[]>(streamId);
                
                // Wrap in OrleansMessageStream
                _myStream = new OrleansMessageStream(Id, orleansStream);
                Logger.LogWarning("DEBUG: Agent {AgentId} using Orleans stream (Namespace: {Namespace})", Id, streamNamespace);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to initialize stream for Agent {AgentId}", Id);
            _myStream = null;
        }
    }

    private IMessageStream GetActorStream(Guid actorId)
    {
        if (_actorStreams.TryGetValue(actorId, out var stream))
        {
            return stream;
        }

        IMessageStream newStream;
        var providerType = _providerOptions.Provider;
        if (_providerOptions.Runtime.TryGetValue("Orleans", out var runtimeProvider))
        {
            providerType = runtimeProvider;
        }

        if (providerType == "MassTransit" && _externalStreamProvider != null)
        {
            newStream = _externalStreamProvider.GetStream(actorId);
        }
        else
        {
            var streamNamespace = _streamingOptions.DefaultStreamNamespace ?? AevatarAgentsOrleansConstants.StreamNamespace;
            var streamId = StreamId.Create(streamNamespace, actorId.ToString());
            var orleansStream = _orleansStreamProvider.GetStream<byte[]>(streamId);
            newStream = new OrleansMessageStream(actorId, orleansStream);
        }

        _actorStreams[actorId] = newStream;
        return newStream;
    }

    /// <summary>
    /// 获取内部的 Grain 引用（兼容性支持）
    /// </summary>
    public IGAgentGrain? GetGrain() => null;

    // ============ 抽象方法实现 ============

    /// <summary>
    /// 发送事件给自己
    /// </summary>
    protected override async Task SendToSelfAsync(EventEnvelope envelope, CancellationToken ct)
    {
        if (_myStream != null)
        {
            await _myStream.ProduceAsync(envelope, ct);
        }
        else
        {
            // Fallback: 直接调用处理
            await HandleEventAsync(envelope, ct);
        }
    }

    /// <summary>
    /// 发送事件到指定 Actor
    /// </summary>
    protected override async Task SendEventToActorAsync(Guid actorId, EventEnvelope envelope, CancellationToken ct)
    {
        try
        {
            var stream = GetActorStream(actorId);
            await stream.ProduceAsync(envelope, ct);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to send event {EventId} to Actor {ActorId}", envelope.Id, actorId);

            // Fallback: 如果是 Orleans 环境，尝试直接通过 Grain 调用 (仅当 stream 失败时)
            // 注意：如果是 MassTransit 模式，这个 fallback 可能不适用，或者依然可以通过 RPC 调用 Grain
            try
            {
                var grain = _grainFactory.GetGrain<IGAgentGrain>(actorId.ToString());
                using var stream = new MemoryStream();
                using var codedOutput = new CodedOutputStream(stream);
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
    /// 激活 Actor - 订阅 Stream
    /// </summary>
    protected override async Task OnActivateAsync(CancellationToken ct)
    {
        Logger.LogInformation("Activating Orleans Actor {ActorId}", Id);

        // 订阅自己的 Stream
        if (_myStream != null)
        {
            try
            {
                _streamSubscription = await _myStream.SubscribeAsync<EventEnvelope>(async envelope =>
                {
                    Logger.LogDebug("Agent {AgentId} received event {EventId} from stream", Id, envelope.Id);
                    await HandleEventAsync(envelope, CancellationToken.None);
                }, ct);
                
                Logger.LogDebug("Successfully subscribed to stream for Agent {AgentId}", Id);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to subscribe to stream for Agent {AgentId}", Id);
            }
        }

        // 如果 Agent 支持事件溯源,触发事件回放
        var agentType = Agent.GetType();
        var baseType = agentType.BaseType;
        while (baseType != null && baseType != typeof(object))
        {
            if (baseType.IsGenericType &&
                baseType.GetGenericTypeDefinition().Name == "GAgentBaseWithEventSourcing`1")
            {
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
    protected override async Task OnDeactivateAsync(CancellationToken ct = default)
    {
        Logger.LogInformation("Deactivating Orleans Actor {ActorId}", Id);

        // 取消 Stream 订阅
        if (_streamSubscription != null)
        {
            try
            {
                await _streamSubscription.UnsubscribeAsync();
                Logger.LogDebug("Successfully unsubscribed from stream for Agent {AgentId}", Id);
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
}
