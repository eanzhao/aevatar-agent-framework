using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.EventSourcing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace Aevatar.Agents.Orleans.EventSourcing;

/// <summary>
/// Orleans Grain 基类，支持 EventSourcing（不依赖 JournaledGrain）
/// </summary>
public abstract class OrleansEventSourcingGrain : Grain, IGAgentGrain
{
    private IGAgent? _agent;
    private IEventStore? _eventStore;
    private readonly ILogger _logger;
    
    protected OrleansEventSourcingGrain(ILogger logger)
    {
        _logger = logger;
    }
    
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        
        // 获取 EventStore
        _eventStore = ServiceProvider?.GetService<IEventStore>() ?? new InMemoryEventStore();
        
        var grainId = this.GetPrimaryKeyString();
        _logger.LogInformation("EventSourcing Grain activated: {GrainId}", grainId);
        
        // 如果 Agent 支持 EventSourcing，重放事件
        if (_agent is GAgentBaseWithEventSourcing<object> esAgent)
        {
            // 注入 EventStore
            var field = esAgent.GetType().BaseType!
                .GetField("_eventStore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(esAgent, _eventStore);
            
            // 重放事件
            await esAgent.OnActivateAsync();
            
            _logger.LogInformation("Replayed events for Grain {GrainId}, Version: {Version}",
                grainId, esAgent.GetCurrentVersion());
        }
    }
    
    public async Task HandleEventAsync(byte[] eventData)
    {
        try
        {
            var envelope = EventEnvelope.Parser.ParseFrom(eventData);
            
            // 处理事件（Agent 需要实现事件处理）
            if (_agent != null && _agent is GAgentBase<object> baseAgent)
            {
                await baseAgent.HandleEventAsync(envelope);
                
                // 如果是 EventSourcing Agent，检查版本
                if (_agent is GAgentBaseWithEventSourcing<object> esAgent)
                {
                    var version = esAgent.GetCurrentVersion();
                    if (version > 0 && version % 100 == 0)
                    {
                        _logger.LogInformation("Grain {GrainId} reached version {Version}, consider snapshot",
                            this.GetPrimaryKeyString(), version);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling event in EventSourcing Grain");
            throw;
        }
    }
    
    public Task<Guid> GetIdAsync()
    {
        return Task.FromResult(Guid.Parse(this.GetPrimaryKeyString()));
    }
    
    public Task InitializeAsync(IGAgent agent)
    {
        _agent = agent;
        return Task.CompletedTask;
    }
    
    // 层级关系管理
    private Guid? _parentId;
    private readonly HashSet<Guid> _childrenIds = new();
    
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
    
    public Task ActivateAsync()
    {
        // Already activated
        return Task.CompletedTask;
    }
    
    public Task DeactivateAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }
}

/// <summary>
/// 具体的 EventSourcing Grain 实现
/// </summary>
public class GenericEventSourcingGrain : OrleansEventSourcingGrain
{
    public GenericEventSourcingGrain(ILogger<GenericEventSourcingGrain> logger) : base(logger)
    {
    }
}
