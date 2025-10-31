using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core.EventSourcing;

/// <summary>
/// 支持 EventSourcing 的 Agent 基类
/// 状态变更通过事件持久化，支持重放
/// </summary>
public abstract class GAgentBaseWithEventSourcing<TState> : GAgentBase<TState>
    where TState : class, new()
{
    private readonly IEventStore? _eventStore;
    private long _currentVersion = 0;
    private const int SnapshotInterval = 100; // 每100个事件做一次快照
    
    protected GAgentBaseWithEventSourcing(
        Guid id,
        IEventStore? eventStore = null,
        ILogger? logger = null)
        : base(id, logger)
    {
        _eventStore = eventStore;
    }
    
    /// <summary>
    /// 触发状态变更事件
    /// </summary>
    protected async Task RaiseStateChangeEventAsync<TEvent>(
        TEvent evt,
        CancellationToken ct = default)
        where TEvent : class, IMessage
    {
        if (_eventStore == null)
        {
            _logger.LogWarning("EventStore not configured, state change event will not be persisted");
            return;
        }
        
        // 创建 StateLogEvent
        _currentVersion++;
        
        using var stream = new MemoryStream();
        using var output = new Google.Protobuf.CodedOutputStream(stream);
        evt.WriteTo(output);
        output.Flush();
        
        var logEvent = new StateLogEvent
        {
            EventId = Guid.NewGuid(),
            AgentId = Id,
            Version = _currentVersion,
            EventType = evt.GetType().AssemblyQualifiedName ?? evt.GetType().FullName ?? evt.GetType().Name,
            EventData = stream.ToArray(),
            TimestampUtc = DateTime.UtcNow
        };
        
        // 持久化事件
        await _eventStore.SaveEventAsync(Id, logEvent, ct);
        
        _logger.LogDebug("State change event persisted: Agent {AgentId}, Version {Version}, Type {EventType}",
            Id, _currentVersion, logEvent.EventType);
        
        // 应用事件到状态
        await ApplyStateChangeEventAsync(evt, ct);
        
        // 检查是否需要快照
        if (_currentVersion % SnapshotInterval == 0)
        {
            await CreateSnapshotAsync(ct);
        }
    }
    
    /// <summary>
    /// 应用状态变更事件（由子类实现）
    /// </summary>
    protected abstract Task ApplyStateChangeEventAsync<TEvent>(TEvent evt, CancellationToken ct = default)
        where TEvent : class, IMessage;
    
    /// <summary>
    /// 从事件存储重放状态
    /// </summary>
    public async Task ReplayEventsAsync(CancellationToken ct = default)
    {
        if (_eventStore == null)
        {
            _logger.LogWarning("EventStore not configured, cannot replay events");
            return;
        }
        
        _logger.LogInformation("Replaying events for Agent {AgentId}", Id);
        
        var events = await _eventStore.GetEventsAsync(Id, ct);
        
        foreach (var logEvent in events.OrderBy(e => e.Version))
        {
            try
            {
                // 反序列化事件
                var eventType = Type.GetType(logEvent.EventType);
                if (eventType == null)
                {
                    _logger.LogWarning("Unknown event type: {EventType}", logEvent.EventType);
                    continue;
                }
                
                // 使用 Protobuf Parser 反序列化
                var parserProperty = eventType.GetProperty("Parser", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (parserProperty != null)
                {
                    var parser = parserProperty.GetValue(null);
                    var parseMethod = parser?.GetType().GetMethod("ParseFrom", new[] { typeof(byte[]) });
                    if (parseMethod != null)
                    {
                        var evt = parseMethod.Invoke(parser, new object[] { logEvent.EventData });
                        
                        // 应用事件
                        var applyMethod = GetType()
                            .GetMethod("ApplyStateChangeEventAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                            ?.MakeGenericMethod(eventType);
                        
                        if (applyMethod != null && evt != null)
                        {
                            await (applyMethod.Invoke(this, new[] { evt, ct }) as Task ?? Task.CompletedTask);
                        }
                    }
                }
                
                _currentVersion = logEvent.Version;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error replaying event {EventId} version {Version}",
                    logEvent.EventId, logEvent.Version);
            }
        }
        
        _logger.LogInformation("Replayed {Count} events, current version: {Version}",
            events.Count, _currentVersion);
    }
    
    /// <summary>
    /// 创建状态快照
    /// </summary>
    protected virtual Task CreateSnapshotAsync(CancellationToken ct = default)
    {
        // 默认实现：记录日志
        // 子类可以重写以实现真正的快照存储
        _logger.LogInformation("Snapshot created for Agent {AgentId} at version {Version}",
            Id, _currentVersion);
        
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// 获取当前版本号
    /// </summary>
    public long GetCurrentVersion() => _currentVersion;
    
    /// <summary>
    /// 重写激活方法，自动重放事件
    /// </summary>
    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        
        // 重放事件恢复状态
        await ReplayEventsAsync(ct);
    }
}

