using Google.Protobuf;

namespace Aevatar.Agents.Orleans;

/// <summary>
/// Orleans Grain接口，用于代理Actor
/// </summary>
public interface IGAgentGrain : IGrainWithGuidKey
{
    /// <summary>
    /// 获取Agent ID
    /// </summary>
    Task<Guid> GetIdAsync();

    /// <summary>
    /// 添加子代理
    /// </summary>
    Task AddSubAgentAsync(Type businessAgentType, Type stateType, Guid subAgentId);

    /// <summary>
    /// 移除子代理
    /// </summary>
    Task RemoveSubAgentAsync(Guid subAgentId);

    /// <summary>
    /// 产生事件
    /// </summary>
    Task ProduceEventAsync(byte[] serializedMessage, string messageTypeName);

    /// <summary>
    /// 订阅父级流
    /// </summary>
    Task SubscribeToParentStreamAsync(Guid parentId);
}

