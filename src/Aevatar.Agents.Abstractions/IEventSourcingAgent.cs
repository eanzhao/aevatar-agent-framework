namespace Aevatar.Agents.Abstractions;

/// <summary>
/// 标记接口，表示 Agent 支持事件溯源
/// </summary>
public interface IEventSourcingAgent
{
    /// <summary>
    /// 指示此 Agent 需要事件溯源支持
    /// </summary>
    bool RequiresEventSourcing => true;
}
