using Proto;

namespace Aevatar.Agents.Runtime.ProtoActor;

/// <summary>
/// Proto.Actor IActor 实现
/// 接收消息并委托给 ProtoActorGAgentActor 处理
/// </summary>
public class AgentActor : IActor
{
    private ProtoActorGAgentActor? _gagentActor;

    public Task ReceiveAsync(IContext context)
    {
        return context.Message switch
        {
            Started => HandleStarted(context),
            SetGAgentActor msg => HandleSetGAgentActor(msg),
            HandleEventMessage msg => HandleEventMessage(msg),
            Stopping => HandleStopping(context),
            _ => Task.CompletedTask
        };
    }

    private Task HandleStarted(IContext context)
    {
        // Actor 启动
        return Task.CompletedTask;
    }

    private Task HandleSetGAgentActor(SetGAgentActor msg)
    {
        // 设置关联的 GAgentActor
        _gagentActor = msg.GAgentActor;
        return Task.CompletedTask;
    }

    private async Task HandleEventMessage(HandleEventMessage msg)
    {
        if (_gagentActor == null)
        {
            throw new InvalidOperationException("GAgentActor not set");
        }

        // 委托给 GAgentActor 处理
        await _gagentActor.HandleEventAsync(msg.Envelope);
    }

    private Task HandleStopping(IContext context)
    {
        // Actor 停止
        return Task.CompletedTask;
    }
}

/// <summary>
/// Proto.Actor 消息：设置 GAgentActor
/// </summary>
public class SetGAgentActor
{
    public required ProtoActorGAgentActor GAgentActor { get; init; }
}

/// <summary>
/// Proto.Actor 消息：处理事件
/// </summary>
public class HandleEventMessage
{
    public required EventEnvelope Envelope { get; init; }
}