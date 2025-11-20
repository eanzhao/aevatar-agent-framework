namespace Aevatar.Agents;

/// <summary>
/// Configuration options for agent streaming
/// </summary>
public class StreamingOptions
{
    /// <summary>
    /// Default stream namespace for agent communication
    /// This should match the Kafka topic name when using Kafka streams
    /// </summary>
    public string DefaultStreamNamespace { get; set; } = "AevatarAgents";
    
    /// <summary>
    /// Stream provider name (e.g., "StreamProvider", "Aevatar")
    /// </summary>
    public string StreamProviderName { get; set; } = "StreamProvider";
    
    /// <summary>
    /// Enable custom stream namespace per agent type
    /// When true, agents can specify their own namespace via attributes or configuration
    /// </summary>
    public bool AllowCustomNamespaces { get; set; } = false;
}

