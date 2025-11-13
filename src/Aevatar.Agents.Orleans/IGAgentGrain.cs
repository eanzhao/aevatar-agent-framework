using Aevatar.Agents.Abstractions;
using Orleans;

namespace Aevatar.Agents.Orleans;

/// <summary>
/// Orleans Grain 接口
/// </summary>
public interface IGAgentGrain : IGrainWithStringKey
{
    /// <summary>
    /// 获取关联的 Agent ID
    /// </summary>
    Task<Guid> GetIdAsync();
    
    /// <summary>
    /// 处理事件（使用 byte[] 以避免 Orleans 序列化问题）
    /// </summary>
    Task HandleEventAsync(byte[] envelopeBytes);
    
    /// <summary>
    /// 发布事件到 Orleans Stream (Kafka)（使用 byte[] 以避免 Orleans 序列化问题）
    /// </summary>
    Task PublishEventAsync(byte[] envelopeBytes);
    
    /// <summary>
    /// 添加子 Agent
    /// </summary>
    Task AddChildAsync(Guid childId);
    
    /// <summary>
    /// 移除子 Agent
    /// </summary>
    Task RemoveChildAsync(Guid childId);
    
    /// <summary>
    /// 设置父 Agent
    /// </summary>
    Task SetParentAsync(Guid parentId);
    
    /// <summary>
    /// 清除父 Agent
    /// </summary>
    Task ClearParentAsync();
    
    /// <summary>
    /// 获取所有子 Agent ID
    /// </summary>
    Task<IReadOnlyList<Guid>> GetChildrenAsync();
    
    /// <summary>
    /// 获取父 Agent ID
    /// </summary>
    Task<Guid?> GetParentAsync();
    
    /// <summary>
    /// 激活并设置Agent类型
    /// </summary>
    /// <param name="agentTypeName">Agent类型的完全限定名</param>
    /// <param name="stateTypeName">State类型的完全限定名</param>
    Task ActivateAsync(string? agentTypeName = null, string? stateTypeName = null);
    
    /// <summary>
    /// 停用
    /// </summary>
    Task DeactivateAsync();
    
    /// <summary>
    /// 获取 Agent 状态（序列化为 byte[]）
    /// </summary>
    Task<byte[]> GetStateAsync();
}
