using Aevatar.Agents.Core;
using Google.Protobuf.WellKnownTypes;
using Kafka.Demo;
using Microsoft.Extensions.Logging;

namespace KafkaStreamDemo;

/// <summary>
/// Kafka Producer Agent that publishes messages to Orleans Stream backed by Kafka
/// Demonstrates how to use Orleans Stream as a Kafka producer
/// </summary>
public class KafkaProducerAgent : GAgentBase<KafkaProducerState>
{
    private readonly string _topic;
    
    public KafkaProducerAgent()
    {
        _topic = "demo-topic";
        InitializeState();
    }
    
    public KafkaProducerAgent(string topic)
    {
        _topic = topic;
        InitializeState();
    }
    
    private void InitializeState()
    {
        State.ProducerId = Id.ToString();
        State.MessagesPublished = 0;
        State.TotalBytesSent = 0;
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Kafka Producer Agent - Publishes messages to topic '{_topic}'");
    }

    /// <summary>
    /// Create a Kafka message event (to be published by Actor layer)
    /// </summary>
    public KafkaMessageEvent CreateMessage(string content)
    {
        var messageId = Guid.NewGuid().ToString();
        
        var kafkaMessage = new KafkaMessageEvent
        {
            MessageId = messageId,
            Topic = _topic,
            Content = content,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            SenderId = Id.ToString()
        };
        
        // Add custom headers
        kafkaMessage.Headers.Add("producer", Id.ToString());
        kafkaMessage.Headers.Add("version", "1.0");
        
        // Update state
        State.MessagesPublished++;
        State.LastPublishTime = Timestamp.FromDateTime(DateTime.UtcNow);
        State.PublishedMessageIds.Add(messageId);
        State.TotalBytesSent += content.Length;
        
        Logger.LogInformation(
            "[KafkaProducer] Created message {MessageId} for topic {Topic}, total: {Total}",
            messageId, _topic, State.MessagesPublished);
        
        return kafkaMessage;
    }

    /// <summary>
    /// Create a batch of messages
    /// </summary>
    public List<KafkaMessageEvent> CreateBatch(IEnumerable<string> contents)
    {
        var messages = new List<KafkaMessageEvent>();
        foreach (var content in contents)
        {
            messages.Add(CreateMessage(content));
        }
        
        Logger.LogInformation(
            "[KafkaProducer] Batch created, total messages: {Total}",
            State.MessagesPublished);
        
        return messages;
    }

    /// <summary>
    /// Create metrics event
    /// </summary>
    public MetricsEvent CreateMetrics()
    {
        var throughput = State.MessagesPublished / 
            (DateTime.UtcNow - State.LastPublishTime.ToDateTime()).TotalSeconds;
        
        var metrics = new MetricsEvent
        {
            AgentId = Id.ToString(),
            MessageCount = State.MessagesPublished,
            ByteCount = State.TotalBytesSent,
            ThroughputMsgsPerSec = throughput,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };
        
        Logger.LogInformation(
            "[KafkaProducer] Metrics created - Messages: {Count}, Bytes: {Bytes}, Throughput: {Throughput:F2} msg/s",
            metrics.MessageCount, metrics.ByteCount, metrics.ThroughputMsgsPerSec);
        
        return metrics;
    }

    /// <summary>
    /// Get current state
    /// </summary>
    public Task<KafkaProducerState> GetStateAsync()
    {
        return Task.FromResult(State);
    }

    /// <summary>
    /// Reset state
    /// </summary>
    public Task ResetAsync()
    {
        State.MessagesPublished = 0;
        State.TotalBytesSent = 0;
        State.PublishedMessageIds.Clear();
        State.LastPublishTime = Timestamp.FromDateTime(DateTime.UtcNow);
        
        Logger.LogInformation("[KafkaProducer] State reset");
        return Task.CompletedTask;
    }
}

