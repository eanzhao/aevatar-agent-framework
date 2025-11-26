namespace Aevatar.Agents.Plugins.MassTransit;

/// <summary>
/// Configuration options for MassTransit message stream.
/// </summary>
public class MassTransitStreamOptions
{
    /// <summary>
    /// Topic prefix for agent events.
    /// </summary>
    public string TopicPrefix { get; set; } = "agent-events";

    /// <summary>
    /// The runtime name to distinguish topics/queues.
    /// </summary>
    public string RuntimeName { get; set; } = "Default";
    
    /// <summary>
    /// Transport type (InMemory, Kafka, RabbitMQ).
    /// </summary>
    public MassTransitTransportType TransportType { get; set; } = MassTransitTransportType.InMemory;
    
    /// <summary>
    /// Kafka configuration.
    /// </summary>
    public KafkaOptions? Kafka { get; set; }
    
    /// <summary>
    /// RabbitMQ configuration.
    /// </summary>
    public RabbitMQOptions? RabbitMQ { get; set; }
}

public enum MassTransitTransportType
{
    InMemory,
    Kafka,
    RabbitMQ
}

public class KafkaOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string ConsumerGroupId { get; set; } = "aevatar-agents";
}

public class RabbitMQOptions
{
    public string Host { get; set; } = "localhost";
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
}

