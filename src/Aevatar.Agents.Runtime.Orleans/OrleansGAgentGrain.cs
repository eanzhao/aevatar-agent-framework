using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Orleans Grain 状态存储模型
/// </summary>
[Serializable]
public class OrleansAgentState
{
    // 父节点 ID
    public Guid? ParentId { get; set; }

    // 子节点 ID 列表
    public List<Guid> Children { get; set; } = new();

    // 构造函数
    public OrleansAgentState() { }

    public OrleansAgentState(Guid? parentId = null)
    {
        ParentId = parentId;
        Children = new List<Guid>();
    }
}

/// <summary>
/// 简化后的 OrleansGAgentGrain
/// 只负责:
/// 1. 存储层级关系(Parent/Children)
/// 2. 接收事件并转发到 OrleansGAgentActor
/// 3. 管理 Orleans Streams 订阅
/// 业务逻辑在 OrleansGAgentActor 中处理
/// </summary>
public class OrleansGAgentGrain : Grain, IStandardGAgentGrain
{
    // Grain 持久化状态
    private readonly IPersistentState<OrleansAgentState> _grainState;

    // Orleans 依赖
    private IStreamProvider? _streamProvider;
    private IAsyncStream<byte[]>? _myStream;  // 使用 byte[] 以避免 JSON 序列化问题，支持 Protobuf ByteString
    private StreamSubscriptionHandle<byte[]>? _streamSubscription;

    // 日志
    private ILogger<OrleansGAgentGrain>? _logger;

    public OrleansGAgentGrain(
        [PersistentState("agentState")]
        IPersistentState<OrleansAgentState> grainState)
    {
        _grainState = grainState;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // 获取 Logger
        _logger = ServiceProvider?.GetService(typeof(ILogger<OrleansGAgentGrain>)) as ILogger<OrleansGAgentGrain>;

        _logger?.LogInformation("Activating OrleansGAgentGrain {GrainId}", this.GetGrainId());

        // 获取 StreamingOptions 配置 (with fallback to default)
        var streamingOptions = ServiceProvider?.GetService(typeof(IOptions<StreamingOptions>)) as IOptions<StreamingOptions>;
        var streamNamespace = streamingOptions?.Value?.DefaultStreamNamespace ?? AevatarAgentsOrleansConstants.StreamNamespace;
        var streamProviderName = streamingOptions?.Value?.StreamProviderName ?? AevatarAgentsOrleansConstants.StreamProviderName;

        // 初始化 Stream Provider (使用配置的 StreamProviderName)
        try
        {
            _streamProvider = this.GetStreamProvider(streamProviderName);

            // 创建自己的 Stream (使用配置的 StreamNamespace)
            // 使用 byte[] 类型以避免 JSON 序列化问题，支持 Protobuf ByteString
            var streamId = StreamId.Create(streamNamespace, this.GetPrimaryKeyString());
            _myStream = _streamProvider.GetStream<byte[]>(streamId);

            // 订阅自己的 Stream
            _streamSubscription = await _myStream.SubscribeAsync(OnStreamEventReceived);

            _logger?.LogDebug("Successfully subscribed to stream for Grain {GrainId}", this.GetGrainId());
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to initialize streams for Grain {GrainId}", this.GetGrainId());
        }

        await base.OnActivateAsync(cancellationToken);
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Deactivating OrleansGAgentGrain {GrainId}", this.GetGrainId());

        // 取消 Stream 订阅
        if (_streamSubscription != null)
        {
            try
            {
                await _streamSubscription.UnsubscribeAsync();
                _logger?.LogDebug("Successfully unsubscribed from stream for Grain {GrainId}", this.GetGrainId());
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to unsubscribe from stream for Grain {GrainId}", this.GetGrainId());
            }
        }

        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    // ============ 基本信息 ============

    /// <summary>
    /// 获取 Grain 的 ID (从字符串键解析为 Guid)
    /// </summary>
    public Task<Guid> GetIdAsync()
    {
        var keyString = this.GetPrimaryKeyString();
        if (Guid.TryParse(keyString, out var guid))
        {
            return Task.FromResult(guid);
        }

        _logger?.LogError("Failed to parse Grain ID from key: {Key}", keyString);
        return Task.FromResult(Guid.Empty);
    }

    // ============ 事件处理 ============

    /// <summary>
    /// 处理从其他 Grain 或 Stream 接收到的事件
    /// 直接转发到本地 Actor 处理
    /// </summary>
    public async Task HandleEventAsync(byte[] envelopeBytes)
    {
        if (envelopeBytes == null || envelopeBytes.Length == 0)
        {
            _logger?.LogWarning("Received empty event bytes in Grain {GrainId}", this.GetGrainId());
            return;
        }

        try
        {
            // 反序列化 EventEnvelope
            var envelope = EventEnvelope.Parser.ParseFrom(envelopeBytes);

            _logger?.LogDebug("Grain {GrainId} received event {EventId}, forwarding to stream", this.GetGrainId(), envelope.Id);

            // 通过 Stream 转发到本地的 OrleansGAgentActor
            // 序列化为 byte[] 以避免 JSON 序列化问题
            if (_myStream != null)
            {
                using var stream = new MemoryStream();
                using var codedOutput = new CodedOutputStream(stream);
                envelope.WriteTo(codedOutput);
                codedOutput.Flush();
                await _myStream.OnNextAsync(stream.ToArray());
            }
            else
            {
                _logger?.LogWarning("Stream not available for Grain {GrainId}, event {EventId} dropped", this.GetGrainId(), envelope.Id);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error handling event in Grain {GrainId}", this.GetGrainId());
            throw;
        }
    }

    /// <summary>
    /// 处理从 Stream 接收到的事件
    /// 这个方法被 Orleans Streams 调用
    /// 反序列化 byte[] 为 EventEnvelope
    /// </summary>
    private async Task OnStreamEventReceived(byte[] envelopeBytes, StreamSequenceToken? token)
    {
        try
        {
            // 反序列化 byte[] 为 EventEnvelope
            var envelope = EventEnvelope.Parser.ParseFrom(envelopeBytes);
            _logger?.LogDebug("Grain {GrainId} received event {EventId} from stream", this.GetGrainId(), envelope.Id);

            // NOTE: 事件会通过 Stream 自动发送到订阅的 OrleansGAgentActor
            // 这里不需要做额外的处理,因为 OrleansGAgentActor 已经订阅了这个 Stream
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error deserializing stream event for Grain {GrainId}", this.GetGrainId());
        }
        
        await Task.CompletedTask;
    }

    // ============ 层级关系管理 ============

    public async Task AddChildAsync(Guid childId)
    {
        if (!_grainState.State.Children.Contains(childId))
        {
            _grainState.State.Children.Add(childId);
            await _grainState.WriteStateAsync();

            _logger?.LogInformation("Added child {ChildId} to Grain {GrainId}", childId, this.GetGrainId());
        }
    }

    public async Task RemoveChildAsync(Guid childId)
    {
        if (_grainState.State.Children.Remove(childId))
        {
            await _grainState.WriteStateAsync();

            _logger?.LogInformation("Removed child {ChildId} from Grain {GrainId}", childId, this.GetGrainId());
        }
    }

    public async Task SetParentAsync(Guid parentId)
    {
        if (_grainState.State.ParentId != parentId)
        {
            _grainState.State.ParentId = parentId;
            await _grainState.WriteStateAsync();

            _logger?.LogInformation("Set parent {ParentId} for Grain {GrainId}", parentId, this.GetGrainId());
        }
    }

    public async Task ClearParentAsync()
    {
        if (_grainState.State.ParentId.HasValue)
        {
            _grainState.State.ParentId = null;
            await _grainState.WriteStateAsync();

            _logger?.LogInformation("Cleared parent for Grain {GrainId}", this.GetGrainId());
        }
    }

    public Task<IReadOnlyList<Guid>> GetChildrenAsync()
    {
        return Task.FromResult<IReadOnlyList<Guid>>(_grainState.State.Children.AsReadOnly());
    }

    public Task<Guid?> GetParentAsync()
    {
        return Task.FromResult(_grainState.State.ParentId);
    }

    // ============ 生命周期管理 ============

    public Task ActivateAsync(string? agentTypeName = null, string? stateTypeName = null)
    {
        // Grain 已经在 OnActivateAsync 中激活
        // 这里可以根据 agentTypeName 和 stateTypeName 做额外的初始化

        _logger?.LogInformation("Grain {GrainId} activated with AgentType: {AgentType}, StateType: {StateType}",
            this.GetGrainId(), agentTypeName ?? "unknown", stateTypeName ?? "unknown");

        return Task.CompletedTask;
    }

    public Task DeactivateAsync()
    {
        // Grain 的停用由 Orleans 管理
        _logger?.LogInformation("Grain {GrainId} deactivate requested", this.GetGrainId());
        return Task.CompletedTask;
    }
}
