using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Proto;

namespace Aevatar.Agents.ProtoActor;

/// <summary>
/// Proto.Actor 运行时的 Message Stream 实现
/// 基于 Actor 消息传递实现 Stream 语义
/// </summary>
public class ProtoActorMessageStream : IMessageStream
{
    private readonly PID _targetPid;
    private readonly IRootContext _rootContext;
    
    public Guid StreamId { get; }
    
    public ProtoActorMessageStream(Guid streamId, PID targetPid, IRootContext rootContext)
    {
        StreamId = streamId;
        _targetPid = targetPid;
        _rootContext = rootContext;
    }
    
    /// <summary>
    /// 发布消息到 Stream（通过 Actor 消息传递）
    /// </summary>
    public async Task ProduceAsync<T>(T message, CancellationToken ct = default) where T : IMessage
    {
        if (message is EventEnvelope envelope)
        {
            // 发送 HandleEventMessage 到目标 Actor
            _rootContext.Send(_targetPid, new HandleEventMessage { Envelope = envelope });
        }
        else
        {
            throw new InvalidOperationException($"ProtoActorMessageStream only supports EventEnvelope, got {typeof(T).Name}");
        }
        
        await Task.CompletedTask;
    }
    
    /// <summary>
    /// 订阅 Stream 消息（Proto.Actor 中由 AgentActor 处理）
    /// </summary>
    public Task SubscribeAsync<T>(Func<T, Task> handler, CancellationToken ct = default) where T : IMessage
    {
        // 在 Proto.Actor 中，订阅由 AgentActor 的 ReceiveAsync 处理
        // 这里不需要额外操作，因为 Actor 已经在接收消息
        return Task.CompletedTask;
    }
}

