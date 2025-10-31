using Google.Protobuf;

namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Agent Actor 运行时抽象接口
/// 负责：层级关系管理、Stream 订阅、事件路由、生命周期管理
/// </summary>
public interface IGAgentActor
{
    /// <summary>
    /// Actor 标识符（与关联的 Agent Id 相同）
    /// </summary>
    Guid Id { get; }
    
    /// <summary>
    /// 获取关联的 Agent 实例
    /// </summary>
    IGAgent GetAgent();
    
    // ============ 层级关系管理 ============
    
    /// <summary>
    /// 添加子 Agent
    /// </summary>
    Task AddChildAsync(Guid childId, CancellationToken ct = default);
    
    /// <summary>
    /// 移除子 Agent
    /// </summary>
    Task RemoveChildAsync(Guid childId, CancellationToken ct = default);
    
    /// <summary>
    /// 设置父 Agent
    /// </summary>
    Task SetParentAsync(Guid parentId, CancellationToken ct = default);
    
    /// <summary>
    /// 清除父 Agent
    /// </summary>
    Task ClearParentAsync(CancellationToken ct = default);
    
    /// <summary>
    /// 获取所有子 Agent ID
    /// </summary>
    Task<IReadOnlyList<Guid>> GetChildrenAsync();
    
    /// <summary>
    /// 获取父 Agent ID
    /// </summary>
    Task<Guid?> GetParentAsync();
    
    // ============ 事件发布和路由 ============
    
    /// <summary>
    /// 发布事件（带路由逻辑）
    /// </summary>
    /// <param name="evt">事件消息</param>
    /// <param name="direction">传播方向</param>
    /// <param name="ct">取消令牌</param>
    /// <typeparam name="TEvent">事件类型</typeparam>
    /// <returns>事件ID</returns>
    Task<string> PublishEventAsync<TEvent>(
        TEvent evt, 
        EventDirection direction = EventDirection.Down,
        CancellationToken ct = default) 
        where TEvent : IMessage;
    
    /// <summary>
    /// 处理接收到的事件
    /// </summary>
    /// <param name="envelope">事件信封</param>
    /// <param name="ct">取消令牌</param>
    Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default);
    
    // ============ 生命周期 ============
    
    /// <summary>
    /// 激活 Actor
    /// </summary>
    Task ActivateAsync(CancellationToken ct = default);
    
    /// <summary>
    /// 停用 Actor
    /// </summary>
    Task DeactivateAsync(CancellationToken ct = default);
}