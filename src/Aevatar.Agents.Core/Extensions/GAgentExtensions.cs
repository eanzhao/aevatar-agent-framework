using Aevatar.Agents.Abstractions;

namespace Aevatar.Agents.Core;

/// <summary>
/// GAgent 扩展方法
/// </summary>
public static class GAgentExtensions
{
    /// <summary>
    /// 获取状态快照（JSON 序列化）
    /// </summary>
    public static string? GetStateSnapshot<TState>(this IGAgent<TState> agent)
        where TState : class, new()
    {
        try
        {
            var state = agent.GetState();
            return System.Text.Json.JsonSerializer.Serialize(state);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取所有事件处理器方法名
    /// </summary>
    public static IEnumerable<string> GetEventHandlerNames(this IGAgent agent)
    {
        if (agent is GAgentBase<object> baseAgent)
        {
            return baseAgent.GetEventHandlers().Select(m => m.Name);
        }

        return [];
    }

    /// <summary>
    /// 获取所有订阅的事件类型
    /// </summary>
    public static IEnumerable<Type> GetSubscribedEventTypes(this IGAgent agent)
    {
        if (agent is GAgentBase<object> baseAgent)
        {
            return baseAgent.GetEventHandlers()
                .Select(m => m.GetParameters().FirstOrDefault()?.ParameterType)
                .Where(t => t != null && t != typeof(EventEnvelope))!;
        }

        return [];
    }
}