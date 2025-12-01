namespace Aevatar.Agents.Plugins.CQRS.Elasticsearch;

/// <summary>
/// Configuration options for Elasticsearch state indexing.
/// </summary>
public class ElasticsearchOptions
{
    /// <summary>
    /// Elasticsearch server URL.
    /// Default: http://localhost:9200
    /// </summary>
    public string Url { get; set; } = "http://localhost:9200";

    /// <summary>
    /// Prefix for index names.
    /// Indices will be named: {IndexPrefix}-{agent-type}
    /// Default: aevatar-state
    /// </summary>
    public string IndexPrefix { get; set; } = "aevatar-state";

    /// <summary>
    /// Number of shards for new indices.
    /// Default: 1
    /// </summary>
    public int NumberOfShards { get; set; } = 1;

    /// <summary>
    /// Number of replicas for new indices.
    /// Default: 0
    /// </summary>
    public int NumberOfReplicas { get; set; } = 0;

    /// <summary>
    /// Request timeout in seconds.
    /// Default: 30
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}

