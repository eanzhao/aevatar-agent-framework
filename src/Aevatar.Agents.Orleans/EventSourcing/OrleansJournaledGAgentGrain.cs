using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.EventSourcing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.EventSourcing;
using Orleans.Providers;
using Orleans.Runtime;

namespace Aevatar.Agents.Orleans.EventSourcing;

/// <summary>
/// Orleans Agent 状态（用于 JournaledGrain）
/// </summary>
[GenerateSerializer]
public class OrleansAgentJournaledState
{
    [Id(0)]
    public Dictionary<string, byte[]> StateData { get; set; } = new();
    
    [Id(1)]
    public long Version { get; set; }
    
    [Id(2)]
    public Guid AgentId { get; set; }
    
    [Id(3)]
    public DateTime LastModifiedUtc { get; set; }
}

/// <summary>
/// Orleans Agent 事件基类（用于 JournaledGrain）
/// </summary>
[GenerateSerializer]
public abstract class OrleansAgentJournaledEvent
{
    [Id(0)]
    public Guid EventId { get; set; } = Guid.NewGuid();
    
    [Id(1)]
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    
    [Id(2)]
    public string EventType { get; set; } = string.Empty;
}

/// <summary>
/// 状态变更事件（用于 JournaledGrain）
/// </summary>
[GenerateSerializer]
public class AgentStateChangedEvent : OrleansAgentJournaledEvent
{
    [Id(0)]
    public byte[] EventData { get; set; } = [];
    
    [Id(1)]
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// 基于 JournaledGrain 的 Orleans Agent Grain
/// </summary>
[LogConsistencyProvider(ProviderName = "LogStorage")]
[StorageProvider(ProviderName = "Default")]
public class OrleansJournaledGAgentGrain : JournaledGrain<OrleansAgentJournaledState, OrleansAgentJournaledEvent>, IJournaledGAgentGrain
{
    private IGAgent? _agent;
    private readonly ILogger<OrleansJournaledGAgentGrain> _logger;
    
    // 层级关系
    private Guid? _parentId;
    private readonly HashSet<Guid> _childrenIds = new();
    
    public OrleansJournaledGAgentGrain(ILogger<OrleansJournaledGAgentGrain> logger)
    {
        _logger = logger;
    }
    
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        
        var grainId = this.GetPrimaryKeyString();
        _logger.LogInformation("JournaledGrain activated: {GrainId}, Version: {Version}",
            grainId, Version);
        
        // 如果 Agent 支持 EventSourcing，同步状态
        if (_agent is GAgentBaseWithEventSourcing<object> esAgent)
        {
            // 从 JournaledGrain 的事件重建状态
            await ReplayAgentEventsAsync(esAgent);
        }
    }
    
    /// <summary>
    /// 处理事件并记录到 Journal
    /// </summary>
    public async Task HandleEventAsync(byte[] eventData)
    {
        try
        {
            var envelope = EventEnvelope.Parser.ParseFrom(eventData);
            
            // 记录事件到 Journal
            var journalEvent = new AgentStateChangedEvent
            {
                EventType = envelope.Payload?.TypeUrl ?? "Unknown",
                EventData = eventData,
                Metadata = new Dictionary<string, string>
                {
                    ["Direction"] = envelope.Direction.ToString(),
                    ["HopCount"] = envelope.CurrentHopCount.ToString()
                }
            };
            
            // 触发事件（会调用 TransitionState）
            RaiseEvent(journalEvent);
            
            // 等待事件被确认写入
            await ConfirmEvents();
            
            // 处理事件（Agent 需要实现事件处理）
            if (_agent != null && _agent is GAgentBase<object> baseAgent)
            {
                await baseAgent.HandleEventAsync(envelope);
                
                // 如果是 EventSourcing Agent，同步版本
                if (_agent is GAgentBaseWithEventSourcing<object> esAgent)
                {
                    var version = esAgent.GetCurrentVersion();
                    _logger.LogDebug("Agent version: {AgentVersion}, Grain version: {GrainVersion}",
                        version, Version);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling event in JournaledGrain");
            throw;
        }
    }
    
    /// <summary>
    /// 状态转换函数（JournaledGrain 核心）
    /// </summary>
    protected override void TransitionState(OrleansAgentJournaledState state, OrleansAgentJournaledEvent @event)
    {
        switch (@event)
        {
            case AgentStateChangedEvent stateChanged:
                state.Version++;
                state.LastModifiedUtc = stateChanged.TimestampUtc;
                
                // 存储事件数据（可选）
                state.StateData[stateChanged.EventType] = stateChanged.EventData;
                
                _logger.LogDebug("State transitioned to version {Version} for event {EventType}",
                    state.Version, stateChanged.EventType);
                break;
                
            default:
                _logger.LogWarning("Unknown event type: {EventType}", @event.GetType().Name);
                break;
        }
    }
    
    /// <summary>
    /// 从 Journal 重放事件到 Agent
    /// </summary>
    private async Task ReplayAgentEventsAsync(GAgentBaseWithEventSourcing<object> esAgent)
    {
        // JournaledGrain 已经自动重放了事件到 State
        // 这里可以将 State 同步到 Agent
        _logger.LogInformation("Replaying {Count} events to Agent from JournaledGrain",
            State.Version);
        
        // 如果需要，可以从 State.StateData 恢复 Agent 状态
        // 这里简化处理，因为 Agent 有自己的 EventStore
        await esAgent.OnActivateAsync();
    }
    
    // IGAgentGrain 实现
    public Task<Guid> GetIdAsync()
    {
        return Task.FromResult(Guid.Parse(this.GetPrimaryKeyString()));
    }
    
    public Task InitializeAsync(IGAgent agent)
    {
        _agent = agent;
        State.AgentId = agent.Id;
        return Task.CompletedTask;
    }
    
    public Task<Guid?> GetParentAsync()
    {
        return Task.FromResult(_parentId);
    }
    
    public Task SetParentAsync(Guid parentId)
    {
        _parentId = parentId;
        return Task.CompletedTask;
    }
    
    public Task ClearParentAsync()
    {
        _parentId = null;
        return Task.CompletedTask;
    }
    
    public Task AddChildAsync(Guid childId)
    {
        _childrenIds.Add(childId);
        return Task.CompletedTask;
    }
    
    public Task RemoveChildAsync(Guid childId)
    {
        _childrenIds.Remove(childId);
        return Task.CompletedTask;
    }
    
    public Task<IReadOnlyList<Guid>> GetChildrenAsync()
    {
        return Task.FromResult<IReadOnlyList<Guid>>(_childrenIds.ToList());
    }
    
    public async Task SendToParentAsync(byte[] eventData)
    {
        if (_parentId.HasValue)
        {
            var parentGrain = GrainFactory.GetGrain<IGAgentGrain>(_parentId.Value.ToString());
            await parentGrain.HandleEventAsync(eventData);
        }
    }
    
    public async Task SendToChildrenAsync(byte[] eventData)
    {
        var tasks = _childrenIds.Select(childId =>
        {
            var childGrain = GrainFactory.GetGrain<IGAgentGrain>(childId.ToString());
            return childGrain.HandleEventAsync(eventData);
        });
        
        await Task.WhenAll(tasks);
    }
    
    public Task ActivateAsync(string? agentTypeName = null, string? stateTypeName = null)
    {
        // Journaled Grain 暂不支持动态创建Agent
        // TODO: 实现动态Agent创建
        return Task.CompletedTask;
    }
    
    public Task DeactivateAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }
    
    // IEventSourcingGAgentGrain 实现
    public Task ReplayEventsAsync()
    {
        // JournaledGrain 自动处理事件重放
        _logger.LogInformation("Grain {GrainId} events are automatically replayed by JournaledGrain", this.GetPrimaryKeyString());
        return Task.CompletedTask;
    }
    
    public Task<int> GetEventCountAsync()
    {
        return Task.FromResult((int)Version);
    }
    
    public async Task CreateSnapshotAsync()
    {
        // 触发快照
        await ConfirmEvents();
        _logger.LogInformation("Grain {GrainId} snapshot created at version {Version}", 
            this.GetPrimaryKeyString(), Version);
    }
    
    // IJournaledGAgentGrain 实现
    public Task<long> GetConfirmedVersionAsync()
    {
        // JournaledGrain 使用 Version 属性
        return Task.FromResult(State.Version);
    }
    
    public Task<long> GetVersionAsync()
    {
        return Task.FromResult(State.Version);
    }
}
