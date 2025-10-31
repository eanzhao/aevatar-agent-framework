namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Agent Actor 工厂接口
/// 用于创建不同运行时的 Actor 实例
/// </summary>
public interface IGAgentActorFactory
{
    /// <summary>
    /// 创建 Agent Actor
    /// </summary>
    /// <param name="id">Agent ID（Guid 类型）</param>
    /// <param name="ct">取消令牌</param>
    /// <typeparam name="TAgent">Agent 类型</typeparam>
    /// <typeparam name="TState">状态类型</typeparam>
    /// <returns>Actor 实例</returns>
    Task<IGAgentActor> CreateAgentAsync<TAgent, TState>(Guid id, CancellationToken ct = default)
        where TAgent : IGAgent<TState>
        where TState : class, new();
}