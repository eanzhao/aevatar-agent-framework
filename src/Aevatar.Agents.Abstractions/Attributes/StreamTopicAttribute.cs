using System;

namespace Aevatar.Agents.Abstractions.Attributes;

/// <summary>
/// Defines the stream topic/namespace for an Agent.
/// Used for auto-discovery and routing in MassTransit/Kafka.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public class StreamTopicAttribute : Attribute
{
    /// <summary>
    /// The topic name or namespace.
    /// </summary>
    public string Topic { get; }

    /// <summary>
    /// Number of partitions for auto-creation (optional).
    /// Default: 8
    /// </summary>
    public int Partitions { get; set; } = 8;

    /// <summary>
    /// Replication factor for auto-creation (optional).
    /// Default: 1
    /// </summary>
    public short ReplicationFactor { get; set; } = 1;

    public StreamTopicAttribute(string topic)
    {
        Topic = topic;
    }
}

