namespace Aevatar.BusinessServer.HttpApi.Host.Extensions;

/// <summary>
/// Agent Runtime Type Enum
/// Defines which runtime implementation to use for agents
/// </summary>
public enum AgentRuntimeType
{
    /// <summary>
    /// Local runtime (in-memory, single process)
    /// Fast, no external dependencies
    /// Perfect for development and testing
    /// </summary>
    Local,

    /// <summary>
    /// Orleans runtime (distributed, clustered)
    /// Persistent state, horizontal scaling
    /// Production-ready for distributed systems
    /// </summary>
    Orleans
}

/// <summary>
/// Agent Runtime Configuration Options
/// Controls which agent runtime implementation to use
/// </summary>
public class AgentRuntimeOptions
{
    public const string SectionName = "AgentRuntime";

    /// <summary>
    /// Runtime type to use
    /// Default: Local (no external dependencies)
    /// </summary>
    public AgentRuntimeType RuntimeType { get; set; } = AgentRuntimeType.Local;

    /// <summary>
    /// Orleans configuration (only used when RuntimeType is Orleans)
    /// </summary>
    public OrleansRuntimeOptions Orleans { get; set; } = new();
}

/// <summary>
/// Orleans Runtime Configuration
/// </summary>
public class OrleansRuntimeOptions
{
    /// <summary>
    /// Cluster ID for Orleans
    /// </summary>
    public string ClusterId { get; set; } = "aevatar-cluster";

    /// <summary>
    /// Service ID for Orleans
    /// </summary>
    public string ServiceId { get; set; } = "aevatar-service";

    /// <summary>
    /// Silo port for Orleans communication
    /// </summary>
    public int SiloPort { get; set; } = 11111;

    /// <summary>
    /// Gateway port for client connections
    /// </summary>
    public int GatewayPort { get; set; } = 30000;

    /// <summary>
    /// Use localhost clustering (development mode)
    /// Set to false for production clustering
    /// </summary>
    public bool UseLocalhostClustering { get; set; } = true;
    
    /// <summary>
    /// Stream provider name for Orleans streams
    /// </summary>
    public string StreamProviderName { get; set; } = "DefaultStreamProvider";
}

