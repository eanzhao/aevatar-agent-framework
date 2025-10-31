using Aevatar.Agents.Abstractions;
using Orleans;

namespace Aevatar.Agents.Orleans;

/// <summary>
/// Orleans Agent Actor 包装器
/// 包装 IGAgentGrain 以实现 IGAgentActor 接口
/// 同时持有本地 Agent 实例以支持 GetAgent()
/// </summary>
public class OrleansGAgentActor : IGAgentActor
{
    private readonly IGAgentGrain _grain;
    private readonly IGAgent _agent;

    public OrleansGAgentActor(IGAgentGrain grain, IGAgent agent)
    {
        _grain = grain;
        _agent = agent;
    }

    public Guid Id => _agent.Id;

    public IGAgent GetAgent()
    {
        // 返回本地 Agent 实例
        return _agent;
    }

    // ============ 层级关系管理 ============

    public Task AddChildAsync(Guid childId, CancellationToken ct = default)
    {
        return _grain.AddChildAsync(childId);
    }

    public Task RemoveChildAsync(Guid childId, CancellationToken ct = default)
    {
        return _grain.RemoveChildAsync(childId);
    }

    public Task SetParentAsync(Guid parentId, CancellationToken ct = default)
    {
        return _grain.SetParentAsync(parentId);
    }

    public Task ClearParentAsync(CancellationToken ct = default)
    {
        return _grain.ClearParentAsync();
    }

    public Task<IReadOnlyList<Guid>> GetChildrenAsync()
    {
        return _grain.GetChildrenAsync();
    }

    public Task<Guid?> GetParentAsync()
    {
        return _grain.GetParentAsync();
    }

    // ============ 事件发布和路由 ============

    public Task<string> PublishEventAsync<TEvent>(
        TEvent evt,
        EventDirection direction = EventDirection.Down,
        CancellationToken ct = default)
        where TEvent : Google.Protobuf.IMessage
    {
        // Orleans Grain 内部处理事件发布
        // 这里需要通过 IEventPublisher 来实现
        // 由于 Grain 已经实现了 IEventPublisher，可以直接调用
        throw new NotSupportedException("Use the Grain's internal event publishing mechanism");
    }

    public Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        // 使用 Protobuf 的序列化方法
        using var stream = new MemoryStream();
        using var output = new Google.Protobuf.CodedOutputStream(stream);
        envelope.WriteTo(output);
        output.Flush();
        return _grain.HandleEventAsync(stream.ToArray());
    }

    // ============ 生命周期 ============

    public Task ActivateAsync(CancellationToken ct = default)
    {
        return _grain.ActivateAsync();
    }

    public Task DeactivateAsync(CancellationToken ct = default)
    {
        return _grain.DeactivateAsync();
    }

    /// <summary>
    /// 获取内部 Grain 引用
    /// </summary>
    public IGAgentGrain GetGrain() => _grain;
}
