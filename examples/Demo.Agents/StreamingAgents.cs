using Aevatar.Agents;
using Aevatar.Agents.Core;
using Aevatar.Agents.Abstractions;
using Microsoft.Extensions.Logging;
using Google.Protobuf.WellKnownTypes;

namespace Demo.Agents;

// 流处理Agent
public class StreamProcessorAgent : GAgentBase<StreamState>
{
    private int _messageCount = 0;
    
    public StreamProcessorAgent(Guid id, ILogger<StreamProcessorAgent>? logger = null) 
        : base(id, logger)
    {
    }
    
    [EventHandler]
    public Task HandleStreamMessage(EventEnvelope envelope)
    {
        _messageCount++;
        Logger?.LogInformation("StreamProcessor {Id} received message #{Count}", Id, _messageCount);
        State.MessagesProcessed++;
        State.LastMessageTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"StreamProcessor {Id}: Processed {_messageCount} messages");
    }
}

// StreamState 已在 demo_messages.proto 中定义

// 发布者Agent
public class PublisherAgent : GAgentBase<PublisherState>
{
    public PublisherAgent(Guid id, ILogger<PublisherAgent>? logger = null) 
        : base(id, logger)
    {
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Publisher {Id}: Published {State.MessagesPublished} messages");
    }
    
    [EventHandler]
    public Task HandlePublishEvent(EventEnvelope envelope)
    {
        State.MessagesPublished++;
        Logger?.LogInformation("Publisher {Id} publishing message #{Count}", Id, State.MessagesPublished);
        return Task.CompletedTask;
    }
}

// PublisherState 已在 demo_messages.proto 中定义

// 订阅者Agent
public class SubscriberAgent : GAgentBase<SubscriberState>
{
    public SubscriberAgent(Guid id, ILogger<SubscriberAgent>? logger = null) 
        : base(id, logger)
    {
    }
    
    [EventHandler]
    public Task HandleSubscribedMessage(EventEnvelope envelope)
    {
        State.MessagesReceived++;
        Logger?.LogInformation("Subscriber {Id} received message #{Count}", Id, State.MessagesReceived);
        return Task.CompletedTask;
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Subscriber {Id}: Received {State.MessagesReceived} messages");
    }
}

// SubscriberState 已在 demo_messages.proto 中定义
