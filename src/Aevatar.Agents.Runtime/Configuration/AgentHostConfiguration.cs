using System.Collections.Generic;

namespace Aevatar.Agents.Runtime;

/// <summary>
/// Configuration for an agent host.
/// </summary>
public class AgentHostConfiguration
{
    /// <summary>
    /// Gets or sets the name of the host.
    /// </summary>
    public string HostName { get; set; } = "default-host";

    /// <summary>
    /// Gets or sets the port number for network-based runtimes.
    /// </summary>
    public int? Port { get; set; }

    /// <summary>
    /// Gets or sets runtime-specific settings.
    /// </summary>
    public Dictionary<string, object> RuntimeSpecificSettings { get; set; } = new();

    /// <summary>
    /// Gets or sets service discovery options.
    /// </summary>
    public ServiceDiscoveryOptions? Discovery { get; set; }

    /// <summary>
    /// Gets or sets streaming options.
    /// </summary>
    public StreamingOptions? Streaming { get; set; }

    /// <summary>
    /// Gets or sets persistence options.
    /// </summary>
    public PersistenceOptions? Persistence { get; set; }

    /// <summary>
    /// Gets or sets clustering options for distributed runtimes.
    /// </summary>
    public ClusteringOptions? Clustering { get; set; }

    /// <summary>
    /// Gets or sets whether to enable health checks.
    /// </summary>
    public bool EnableHealthChecks { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to enable metrics collection.
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of agents this host can manage.
    /// </summary>
    public int? MaxAgents { get; set; }
}

/// <summary>
/// Options for service discovery.
/// </summary>
public class ServiceDiscoveryOptions
{
    /// <summary>
    /// Gets or sets the service discovery provider (e.g., "Consul", "Kubernetes", "Static").
    /// </summary>
    public string Provider { get; set; } = "Static";

    /// <summary>
    /// Gets or sets the connection string or endpoint for the discovery service.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets additional provider-specific settings.
    /// </summary>
    public Dictionary<string, object> Settings { get; set; } = new();
}

/// <summary>
/// Options for streaming configuration.
/// </summary>
public class StreamingOptions
{
    /// <summary>
    /// Gets or sets the streaming provider (e.g., "Memory", "EventHub", "Kafka").
    /// </summary>
    public string Provider { get; set; } = "Memory";

    /// <summary>
    /// Gets or sets the connection string for the streaming service.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the default stream namespace.
    /// </summary>
    public string DefaultNamespace { get; set; } = "AevatarStreams";

    /// <summary>
    /// Gets or sets additional provider-specific settings.
    /// </summary>
    public Dictionary<string, object> Settings { get; set; } = new();
}

/// <summary>
/// Options for persistence configuration.
/// </summary>
public class PersistenceOptions
{
    /// <summary>
    /// Gets or sets the persistence provider (e.g., "Memory", "AzureStorage", "CosmosDB").
    /// </summary>
    public string Provider { get; set; } = "Memory";

    /// <summary>
    /// Gets or sets the connection string for the persistence service.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets whether to enable automatic state persistence.
    /// </summary>
    public bool AutoPersist { get; set; } = true;

    /// <summary>
    /// Gets or sets the persistence interval in seconds.
    /// </summary>
    public int PersistIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets additional provider-specific settings.
    /// </summary>
    public Dictionary<string, object> Settings { get; set; } = new();
}

/// <summary>
/// Options for clustering in distributed runtimes.
/// </summary>
public class ClusteringOptions
{
    /// <summary>
    /// Gets or sets the clustering provider (e.g., "Localhost", "AzureStorage", "Consul").
    /// </summary>
    public string Provider { get; set; } = "Localhost";

    /// <summary>
    /// Gets or sets the cluster identifier.
    /// </summary>
    public string ClusterId { get; set; } = "default-cluster";

    /// <summary>
    /// Gets or sets the service identifier within the cluster.
    /// </summary>
    public string ServiceId { get; set; } = "default-service";

    /// <summary>
    /// Gets or sets the connection string for the clustering service.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets additional provider-specific settings.
    /// </summary>
    public Dictionary<string, object> Settings { get; set; } = new();
}
