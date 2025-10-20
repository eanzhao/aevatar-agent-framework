using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Proto;

namespace Aevatar.Agents.ProtoActor;

/// <summary>
/// Proto.Actor actor实现，用于处理代理消息
/// </summary>
public class AgentActor<TState> : IActor where TState : class, new()
{
    private readonly IGAgent<TState> _agent;
    private readonly IMessageSerializer _serializer;
    private readonly IMessageStream _stream;
    private readonly Dictionary<Guid, List<EventEnvelope>> _eventStore;

    public AgentActor(
        IGAgent<TState> agent, 
        IMessageSerializer serializer, 
        IMessageStream stream,
        Dictionary<Guid, List<EventEnvelope>> eventStore)
    {
        _agent = agent;
        _serializer = serializer;
        _stream = stream;
        _eventStore = eventStore;
        
        // 确保事件存储中有当前代理的条目
        if (!_eventStore.ContainsKey(_agent.Id))
        {
            _eventStore[_agent.Id] = new List<EventEnvelope>();
        }
    }

    public Task ReceiveAsync(IContext context)
    {
        return context.Message switch
        {
            Started => HandleStarted(context),
            ProtoActorMessage protoMessage => HandleProtoActorMessage(context, protoMessage),
            Stopping => HandleStopping(context),
            _ => Task.CompletedTask
        };
    }

    private Task HandleStarted(IContext context)
    {
        // Actor启动时的初始化逻辑
        return Task.CompletedTask;
    }
    
    private async Task HandleProtoActorMessage(IContext context, ProtoActorMessage message)
    {
        try
        {
            // 获取消息内容
            var msgContent = message.GetMessage(_serializer);
            
            // 处理事件信封
            if (msgContent is EventEnvelope envelope)
            {
                await _agent.ApplyEventAsync(envelope);
                
                // 存储事件到事件存储
                _eventStore[_agent.Id].Add(envelope);
            }
            
            // 处理其他消息类型可以在这里添加
        }
        catch (Exception ex)
        {
            // 处理异常，可以通过Actor消息机制报告错误
            context.Respond(new ProtoActorError { ErrorMessage = ex.Message });
        }
    }
    
    private Task HandleStopping(IContext context)
    {
        // Actor停止时的清理逻辑
        return Task.CompletedTask;
    }
}
