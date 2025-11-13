using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Google.Protobuf.WellKnownTypes;
using Kafka.Demo;
using Microsoft.Extensions.Logging;

namespace KafkaStreamDemo;

/// <summary>
/// Kafka Consumer Agent that receives messages from Orleans Stream backed by Kafka
/// Demonstrates how to use Orleans Stream as a Kafka consumer with event handlers
/// </summary>
public class KafkaConsumerAgent : GAgentBase<KafkaConsumerState>
{
    public KafkaConsumerAgent()
    {
        State.ConsumerId = Id.ToString();
        State.MessagesConsumed = 0;
        State.TotalBytesReceived = 0;
        State.SubscriptionStatus = "active";
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Kafka Consumer Agent - Consumes and processes messages from Kafka stream");
    }

    /// <summary>
    /// Handle incoming Kafka messages
    /// This event handler will be automatically invoked when messages arrive from Kafka stream
    /// </summary>
    [EventHandler(Priority = 1, AllowSelfHandling = false)]
    public async Task HandleKafkaMessage(KafkaMessageEvent message)
    {
        Logger.LogInformation(
            "[KafkaConsumer] Received message {MessageId} from {Sender} on topic {Topic}",
            message.MessageId, message.SenderId, message.Topic);
        
        Logger.LogDebug(
            "[KafkaConsumer] Message content: {Content}, Headers: {Headers}",
            message.Content, string.Join(", ", message.Headers.Select(h => $"{h.Key}={h.Value}")));
        
        // Process message
        await ProcessMessageAsync(message);
        
        // Update state
        State.MessagesConsumed++;
        State.LastConsumeTime = Timestamp.FromDateTime(DateTime.UtcNow);
        State.ConsumedMessageIds.Add(message.MessageId);
        State.TotalBytesReceived += message.Content.Length;
        
        Logger.LogInformation(
            "[KafkaConsumer] Processed message {MessageId}, total consumed: {Total}",
            message.MessageId, State.MessagesConsumed);
    }

    /// <summary>
    /// Handle metrics events
    /// </summary>
    [EventHandler(Priority = 2, AllowSelfHandling = false)]
    public Task HandleMetrics(MetricsEvent metrics)
    {
        Logger.LogInformation(
            "[KafkaConsumer] Received metrics from {AgentId} - Count: {Count}, Throughput: {Throughput:F2} msg/s",
            metrics.AgentId, metrics.MessageCount, metrics.ThroughputMsgsPerSec);
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handle stream control events
    /// </summary>
    [EventHandler(Priority = 3, AllowSelfHandling = false)]
    public Task HandleStreamControl(StreamControlEvent control)
    {
        Logger.LogInformation(
            "[KafkaConsumer] Received control event: {Action} for {Target}",
            control.Action, control.TargetId);
        
        State.SubscriptionStatus = control.Action switch
        {
            "pause" => "paused",
            "resume" => "active",
            "stop" => "stopped",
            _ => State.SubscriptionStatus
        };
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// Process the received message
    /// Can be overridden for custom processing logic
    /// </summary>
    protected virtual Task ProcessMessageAsync(KafkaMessageEvent message)
    {
        // Default processing: log the message
        Logger.LogDebug(
            "[KafkaConsumer] Processing message: {MessageId} - {Content}",
            message.MessageId, message.Content);
        
        // Simulate some processing work
        return Task.Delay(10);
    }

    /// <summary>
    /// Get current state
    /// </summary>
    public Task<KafkaConsumerState> GetStateAsync()
    {
        return Task.FromResult(State);
    }

    /// <summary>
    /// Get consumption statistics
    /// </summary>
    public Task<(int MessagesConsumed, long BytesReceived, double AvgMessageSize)> GetStatsAsync()
    {
        var avgSize = State.MessagesConsumed > 0 
            ? (double)State.TotalBytesReceived / State.MessagesConsumed 
            : 0;
        
        return Task.FromResult((
            State.MessagesConsumed, 
            State.TotalBytesReceived, 
            avgSize));
    }

    /// <summary>
    /// Reset state
    /// </summary>
    public Task ResetAsync()
    {
        State.MessagesConsumed = 0;
        State.TotalBytesReceived = 0;
        State.ConsumedMessageIds.Clear();
        State.LastConsumeTime = Timestamp.FromDateTime(DateTime.UtcNow);
        State.SubscriptionStatus = "active";
        
        Logger.LogInformation("[KafkaConsumer] State reset");
        return Task.CompletedTask;
    }
}

