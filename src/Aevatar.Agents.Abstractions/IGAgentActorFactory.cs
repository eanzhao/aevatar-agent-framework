namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Agent Actor 工厂接口
/// 用于创建不同运行时的 Actor 实例
/// </summary>
public interface IGAgentActorFactory
{
    /// <summary>
    /// 创建 Agent Actor（自动推断状态类型）
    /// </summary>
    /// <param name="id">Agent ID（Guid 类型）</param>
    /// <param name="ct">取消令牌</param>
    /// <typeparam name="TAgent">Agent 类型</typeparam>
    /// <returns>Actor 实例</returns>
    Task<IGAgentActor> CreateGAgentActorAsync<TAgent>(Guid id, CancellationToken ct = default)
        where TAgent : IGAgent;
}