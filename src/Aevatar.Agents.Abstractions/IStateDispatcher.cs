namespace Aevatar.Agents.Abstractions;

/// <summary>
/// 状态分发器接口
/// 当 Agent 状态变更时，自动发布到订阅者
/// </summary>
public interface IStateDispatcher
{
    /// <summary>
    /// 发布单个状态变更
    /// </summary>
    Task PublishSingleAsync<TState>(Guid agentId, StateSnapshot<TState> snapshot, CancellationToken ct = default)
        where TState : class, new();

    /// <summary>
    /// 发布批量状态变更
    /// </summary>
    Task PublishBatchAsync<TState>(Guid agentId, StateSnapshot<TState> snapshot, CancellationToken ct = default)
        where TState : class, new();

    /// <summary>
    /// 订阅 Agent 的状态变更
    /// </summary>
    Task SubscribeAsync<TState>(Guid agentId, Func<StateSnapshot<TState>, Task> handler, CancellationToken ct = default)
        where TState : class, new();
}

/// <summary>
/// 状态快照
/// 包含状态、版本和时间戳
/// </summary>
public class StateSnapshot<TState> where TState : class, new()
{
    public Guid AgentId { get; set; }
    public TState State { get; set; } = new();
    public long Version { get; set; }
    public DateTime TimestampUtc { get; set; }

    public StateSnapshot()
    {
    }

    public StateSnapshot(Guid agentId, TState state, long version)
    {
        AgentId = agentId;
        State = state;
        Version = version;
        TimestampUtc = DateTime.UtcNow;
    }
}