using System;
using System.Collections.Generic;

namespace Aevatar.Agents.Plugins.MassTransit;

/// <summary>
/// MassTransit Stream 配置选项
/// </summary>
public class MassTransitStreamOptions
{
    /// <summary>
    /// Topic 前缀（用于生成 Agent 的 Topic，作为默认 Topic）
    /// </summary>
    public string TopicPrefix { get; set; } = "agent-events";

    /// <summary>
    /// 动态 Topic 映射表
    /// Key: Category (Agent Type Name)
    /// Value: Kafka Topic Name
    /// </summary>
    public Dictionary<string, string> TopicMapping { get; set; } = new();

    /// <summary>
    /// 需要监听的额外 Topic 列表（用于多租户或多业务类型隔离）
    /// 注意：如果配置了 TopicMapping，Silo 启动时会自动将 Mapping 中的 Values 加入监听列表，无需重复在此配置。
    /// </summary>
    public List<string> Topics { get; set; } = new();

    /// <summary>
    /// 传输方式：InMemory, Kafka, RabbitMQ
    /// </summary>
    public MassTransitTransportType TransportType { get; set; } = MassTransitTransportType.InMemory;

    /// <summary>
    /// 当前 Runtime 的名称，用于 Topic 命名区分
    /// </summary>
    public string RuntimeName { get; set; } = "Default";

    /// <summary>
    /// Kafka 配置（当 TransportType = Kafka 时使用）
    /// </summary>
    public KafkaOptions? Kafka { get; set; }

    /// <summary>
    /// RabbitMQ 配置（当 TransportType = RabbitMQ 时使用）
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
    // 可以添加更多 Kafka Producer/Consumer 配置
}

public class RabbitMQOptions
{
    public string Host { get; set; } = "localhost";
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    // 可以添加更多 RabbitMQ 配置
}
