using Orleans;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Orleans Event Sourcing Grain 接口
/// 用于支持事件溯源的 Agent Grain
/// </summary>
public interface IEventSourcingGAgentGrain : IGAgentGrain
{
    /// <summary>
    /// 重放事件以重建状态
    /// </summary>
    Task ReplayEventsAsync();
    
    /// <summary>
    /// 获取事件历史数量
    /// </summary>
    Task<int> GetEventCountAsync();
    
    /// <summary>
    /// 创建状态快照
    /// </summary>
    Task CreateSnapshotAsync();
}

/// <summary>
/// Orleans Journaled Grain 接口
/// 用于使用 Orleans JournaledGrain 的 Agent
/// </summary>
public interface IJournaledGAgentGrain : IEventSourcingGAgentGrain
{
    /// <summary>
    /// 获取确认的版本号
    /// </summary>
    Task<long> GetConfirmedVersionAsync();
    
    /// <summary>
    /// 获取当前版本号
    /// </summary>
    Task<long> GetVersionAsync();
}
