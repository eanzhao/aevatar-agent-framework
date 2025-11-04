using Aevatar.Agents;
using Aevatar.Agents.Core;
using Aevatar.Agents.Abstractions;
using Microsoft.Extensions.Logging;
using Google.Protobuf.WellKnownTypes;

namespace Demo.Agents;

// 路由Agent
public class RouterAgent : GAgentBase<RouterState>
{
    public RouterAgent(Guid id, ILogger<RouterAgent>? logger = null) 
        : base(id, logger)
    {
    }
    
    [EventHandler]
    public Task HandleRoutingMessage(RoutingMessage message)
    {
        State.MessagesRouted++;
        Logger?.LogInformation("Router {Id} routing message {MessageId} with routing info: {RoutingInfo}", 
            Id, message.Id, message.RoutingInfo);
        return Task.CompletedTask;
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Router {Id}: Routed {State.MessagesRouted} messages");
    }
}

// RouterState 已在 demo_messages.proto 中定义

// 处理器Agent
public class ProcessorAgent : GAgentBase<ProcessorState>
{
    public ProcessorAgent(Guid id, ILogger<ProcessorAgent>? logger = null) 
        : base(id, logger)
    {
    }
    
    [AllEventHandler]
    public Task ProcessEvent(EventEnvelope envelope)
    {
        State.MessagesProcessed++;
        State.LastProcessed = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
        Logger?.LogInformation("Processor {Id} processed event #{Count}", Id, State.MessagesProcessed);
        return Task.CompletedTask;
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Processor {Id}: Processed {State.MessagesProcessed} events");
    }
}

// ProcessorState 已在 demo_messages.proto 中定义

// 过滤器Agent
public class FilterAgent : GAgentBase<FilterState>
{
    public FilterAgent(Guid id, ILogger<FilterAgent>? logger = null) 
        : base(id, logger)
    {
    }
    
    [AllEventHandler]
    public Task FilterEvent(EventEnvelope envelope)
    {
        State.MessagesFiltered++;
        
        // 根据优先级过滤（使用Message字段）
        if (envelope.Message?.Contains("high") == true)
        {
            State.MessagesPassed++;
            Logger?.LogInformation("Filter {Id} passing high priority message", Id);
            // 继续传播
            return Task.CompletedTask;
        }
        else
        {
            State.MessagesFiltered++;
            Logger?.LogInformation("Filter {Id} filtered out low priority message", Id);
            // 停止传播
            envelope.ShouldStopPropagation = true;
            return Task.CompletedTask;
        }
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Filter {Id}: {State.MessagesFiltered} filtered, {State.MessagesPassed} passed");
    }
}

// FilterState 已在 demo_messages.proto 中定义

// 日志Agent
public class LoggerAgent : GAgentBase<LoggerState>
{
    public LoggerAgent(Guid id, ILogger<LoggerAgent>? logger = null) 
        : base(id, logger)
    {
    }
    
    [AllEventHandler]
    public Task LogEvent(EventEnvelope envelope)
    {
        State.MessagesLogged++;
        if (!string.IsNullOrEmpty(envelope.Message))
        {
            State.RecentLogs.Add(envelope.Message);
            if (State.RecentLogs.Count > 10) // 保留最近10条日志
            {
                State.RecentLogs.RemoveAt(0);
            }
        }
        Logger?.LogInformation("Logger {Id} logged event: {Message}", Id, envelope.Message);
        return Task.CompletedTask;
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Logger {Id}: Logged {State.MessagesLogged} events");
    }
}

// LoggerState 已在 demo_messages.proto 中定义

// 广播Agent
public class BroadcastAgent : GAgentBase<BroadcastState>
{
    public BroadcastAgent(Guid id, ILogger<BroadcastAgent>? logger = null) 
        : base(id, logger)
    {
    }
    
    [EventHandler]
    public Task HandleBroadcast(BroadcastMessage broadcast)
    {
        State.MessagesBroadcast++;
        State.ReceiverCount = 1; // 自己是接收者
        Logger?.LogInformation("BroadcastAgent {Id} received broadcast on topic {Topic}: {Content}", 
            Id, broadcast.Topic, broadcast.Content);
        return Task.CompletedTask;
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"BroadcastAgent {Id}: Broadcast {State.MessagesBroadcast} messages to {State.ReceiverCount} receivers");
    }
}

// BroadcastState 已在 demo_messages.proto 中定义
