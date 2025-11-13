using System;
using System.IO;
using System.Linq;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Orleans;

namespace Aevatar.Agents.Runtime.Orleans;

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

    public async Task<string> PublishEventAsync<TEvent>(
        TEvent evt,
        EventDirection direction = EventDirection.Down,
        CancellationToken ct = default)
        where TEvent : Google.Protobuf.IMessage
    {
        // 如果事件已经是 EventEnvelope，直接使用
        EventEnvelope envelope;
        if (evt is EventEnvelope env)
        {
            envelope = env;
        }
        else
        {
            // 否则包装成 EventEnvelope
            envelope = new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Payload = Google.Protobuf.WellKnownTypes.Any.Pack(evt),
                Direction = direction,
                PublisherId = _agent.Id.ToString(),
                Version = 1,
                CorrelationId = Guid.NewGuid().ToString(),
                ShouldStopPropagation = false,
                MaxHopCount = -1,
                CurrentHopCount = 0,
                MinHopCount = -1
            };
            envelope.Publishers.Add(_agent.Id.ToString());
        }
        
        // Use Orleans Streams (Kafka) for event propagation
        // Serialize the envelope to bytes
        using var stream = new MemoryStream();
        using var output = new Google.Protobuf.CodedOutputStream(stream);
        envelope.WriteTo(output);
        output.Flush();
        var envelopeBytes = stream.ToArray();
        
        // Publish through Grain to Orleans Stream (Kafka)
        // This sends the event to the agent's own stream, which all subscribers will receive
        await _grain.PublishEventAsync(envelopeBytes);
        
        return envelope.Id;
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
        // 获取Agent的类型信息
        var agentType = _agent.GetType();
        var stateType = _agent.GetType().BaseType?.GetGenericArguments().FirstOrDefault() ?? typeof(object);
        
        return _grain.ActivateAsync(agentType.AssemblyQualifiedName, stateType.AssemblyQualifiedName);
    }

    public Task DeactivateAsync(CancellationToken ct = default)
    {
        return _grain.DeactivateAsync();
    }

    /// <summary>
    /// 获取内部 Grain 引用
    /// </summary>
    public IGAgentGrain GetGrain() => _grain;
    
    /// <summary>
    /// 从 Grain 获取状态（适用于需要读取最新状态的场景）
    /// </summary>
    public async Task<TState?> GetStateFromGrainAsync<TState>() where TState : Google.Protobuf.IMessage, new()
    {
        var stateBytes = await _grain.GetStateAsync();
        if (stateBytes == null || stateBytes.Length == 0)
        {
            return default;
        }
        
        var state = new TState();
        using var stream = new MemoryStream(stateBytes);
        using var input = new Google.Protobuf.CodedInputStream(stream);
        state.MergeFrom(input);
        return state;
    }
}
