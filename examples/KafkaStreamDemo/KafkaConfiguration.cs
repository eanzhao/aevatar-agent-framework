namespace KafkaStreamDemo;

/// <summary>
/// Kafka configuration options loaded from appsettings.json
/// </summary>
public class KafkaConfiguration
{
    public List<string> Brokers { get; set; } = new() { "localhost:9092" };
    public string ConsumerGroupId { get; set; } = "aevatar-consumer-group";
    public string ConsumeMode { get; set; } = "LastCommittedMessage";
    public int PollTimeoutMs { get; set; } = 100;
    public List<KafkaTopicConfiguration> Topics { get; set; } = new();
    public KafkaPerformanceConfiguration Performance { get; set; } = new();
}

public class KafkaTopicConfiguration
{
    public string Name { get; set; } = string.Empty;
    public int Partitions { get; set; } = 8;
    public short ReplicationFactor { get; set; } = 1;
    public bool AutoCreate { get; set; } = true;
}

public class KafkaPerformanceConfiguration
{
    public int GetQueueMsgsTimerPeriodMs { get; set; } = 50;
    public int MessageBatchSize { get; set; } = 100;
}

