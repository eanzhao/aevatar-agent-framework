namespace Aevatar.Agents.Abstractions.EventSourcing;

/// <summary>
/// 事件存储接口
/// 用于持久化和读取 StateLogEvent
/// </summary>
public interface IEventStore
{
    /// <summary>
    /// 保存状态变更事件
    /// </summary>
    Task SaveEventAsync(Guid agentId, StateLogEvent logEvent, CancellationToken ct = default);

    /// <summary>
    /// 批量保存事件
    /// </summary>
    Task SaveEventsAsync(Guid agentId, IEnumerable<StateLogEvent> logEvents, CancellationToken ct = default);

    /// <summary>
    /// 读取 Agent 的所有事件（用于重放）
    /// </summary>
    Task<IReadOnlyList<StateLogEvent>> GetEventsAsync(Guid agentId, CancellationToken ct = default);

    /// <summary>
    /// 读取指定版本范围的事件
    /// </summary>
    Task<IReadOnlyList<StateLogEvent>> GetEventsAsync(
        Guid agentId,
        long fromVersion,
        long toVersion,
        CancellationToken ct = default);

    /// <summary>
    /// 获取最新版本号
    /// </summary>
    Task<long> GetLatestVersionAsync(Guid agentId, CancellationToken ct = default);

    /// <summary>
    /// 清除 Agent 的所有事件
    /// </summary>
    Task ClearEventsAsync(Guid agentId, CancellationToken ct = default);
}

/// <summary>
/// 状态日志事件
/// Agent 状态变更的原子事件
/// </summary>
public class StateLogEvent
{
    public Guid EventId { get; set; } = Guid.NewGuid();
    public Guid AgentId { get; set; }
    public long Version { get; set; }
    public string EventType { get; set; } = string.Empty;
    public byte[] EventData { get; set; } = Array.Empty<byte>();
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string? Metadata { get; set; }
}
