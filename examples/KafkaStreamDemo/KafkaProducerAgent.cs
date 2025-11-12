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
    /// Publish a message to Kafka through Orleans Stream
    /// </summary>
    public async Task PublishMessageAsync(string content)
    {
        var messageId = Guid.NewGuid().ToString();
        
        var kafkaMessage = new KafkaMessageEvent
        {
            MessageId = messageId,
            Topic = _topic,
            Content = content,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            SenderId = AgentId
        };
        
        // Add custom headers
        kafkaMessage.Headers.Add("producer", AgentId);
        kafkaMessage.Headers.Add("version", "1.0");
        
        // Publish to stream (Orleans Stream will route to Kafka)
        await PublishAsync(kafkaMessage);
        
        // Update state
        State.MessagesPublished++;
        State.LastPublishTime = Timestamp.FromDateTime(DateTime.UtcNow);
        State.PublishedMessageIds.Add(messageId);
        State.TotalBytesSent += content.Length;
        
        Logger.LogInformation(
            "[KafkaProducer] Published message {MessageId} to topic {Topic}, total: {Total}",
            messageId, _topic, State.MessagesPublished);
    }

    /// <summary>
    /// Publish a batch of messages
    /// </summary>
    public async Task PublishBatchAsync(IEnumerable<string> contents)
    {
        foreach (var content in contents)
        {
            await PublishMessageAsync(content);
        }
        
        Logger.LogInformation(
            "[KafkaProducer] Batch published, total messages: {Total}",
            State.MessagesPublished);
    }

    /// <summary>
    /// Publish metrics event
    /// </summary>
    public async Task PublishMetricsAsync()
    {
        var throughput = State.MessagesPublished / 
            (DateTime.UtcNow - State.LastPublishTime.ToDateTime()).TotalSeconds;
        
        var metrics = new MetricsEvent
        {
            AgentId = AgentId,
            MessageCount = State.MessagesPublished,
            ByteCount = State.TotalBytesSent,
            ThroughputMsgsPerSec = throughput,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };
        
        await PublishAsync(metrics);
        
        Logger.LogInformation(
            "[KafkaProducer] Metrics published - Messages: {Count}, Bytes: {Bytes}, Throughput: {Throughput:F2} msg/s",
            metrics.MessageCount, metrics.ByteCount, metrics.ThroughputMsgsPerSec);
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

