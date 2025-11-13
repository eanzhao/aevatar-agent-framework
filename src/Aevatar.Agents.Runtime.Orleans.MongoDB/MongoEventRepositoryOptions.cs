namespace Aevatar.Agents.Orleans.MongoDB;

/// <summary>
/// Configuration options for MongoEventRepository
/// </summary>
public class MongoEventRepositoryOptions
{
    /// <summary>
    /// Database name for event storage
    /// </summary>
    public string DatabaseName { get; set; } = "OrleansEventStore";
    
    /// <summary>
    /// Collection name for this agent type's events
    /// Pattern: {AgentType}Events (e.g., "BankAccountEvents")
    /// </summary>
    public string CollectionName { get; set; } = "Events";
    
    /// <summary>
    /// Maximum connection pool size
    /// Default: 100
    /// </summary>
    public int MaxConnectionPoolSize { get; set; } = 100;
    
    /// <summary>
    /// Minimum connection pool size
    /// Default: 10
    /// </summary>
    public int MinConnectionPoolSize { get; set; } = 10;
    
    /// <summary>
    /// Server selection timeout
    /// Default: 30 seconds
    /// </summary>
    public TimeSpan ServerSelectionTimeout { get; set; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// Maximum time to wait for a connection
    /// Default: 30 seconds
    /// </summary>
    public TimeSpan MaxConnectionIdleTime { get; set; } = TimeSpan.FromMinutes(10);
    
    /// <summary>
    /// Enable detailed logging for MongoDB operations
    /// Default: false (production)
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = false;
}

