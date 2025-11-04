namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Agent Actor 管理器接口
/// 负责全局 Actor 的注册、查找和生命周期管理
/// </summary>
public interface IGAgentActorManager
{
    /// <summary>
    /// 创建并注册 Agent Actor
    /// </summary>
    Task<IGAgentActor> CreateAndRegisterAsync<TAgent, TState>(
        Guid id,
        CancellationToken ct = default)
        where TAgent : IStateGAgent<TState>
        where TState : class, new();
    
    /// <summary>
    /// 获取已注册的 Actor
    /// </summary>
    Task<IGAgentActor?> GetActorAsync(Guid id);
    
    /// <summary>
    /// 获取所有已注册的 Actor
    /// </summary>
    Task<IReadOnlyList<IGAgentActor>> GetAllActorsAsync();
    
    /// <summary>
    /// 停用并注销 Actor
    /// </summary>
    Task DeactivateAndUnregisterAsync(Guid id, CancellationToken ct = default);
    
    /// <summary>
    /// 批量停用所有 Actor
    /// </summary>
    Task DeactivateAllAsync(CancellationToken ct = default);
    
    /// <summary>
    /// 检查 Actor 是否存在
    /// </summary>
    Task<bool> ExistsAsync(Guid id);
    
    /// <summary>
    /// 获取 Actor 数量
    /// </summary>
    Task<int> GetCountAsync();
}

