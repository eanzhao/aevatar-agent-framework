namespace Aevatar.Agents.Abstractions;

// ============================================================
//  Agent Manager - Interface Segregation
//  Split into focused interfaces for better testability
// ============================================================

#region Core Interfaces (ISP compliant)

/// <summary>
/// Agent type registry - read-only type discovery
/// </summary>
public interface IAgentTypeRegistry
{
    /// <summary>
    /// Get all available agent types
    /// </summary>
    List<Type> GetAvailableAgentTypes();

    /// <summary>
    /// Get all available event types
    /// </summary>
    List<Type> GetAvailableEventTypes();

    /// <summary>
    /// Check if the type is a valid agent type
    /// </summary>
    bool IsValidAgentType(Type type);

    /// <summary>
    /// Check if the type is a valid event type
    /// </summary>
    bool IsValidEventType(Type type);
}

/// <summary>
/// Agent type registrar - write operations for type registration
/// </summary>
public interface IAgentTypeRegistrar
{
    /// <summary>
    /// Register an agent type (for plugin system)
    /// </summary>
    void RegisterAgentType(Type agentType);

    /// <summary>
    /// Unregister an agent type
    /// </summary>
    void UnregisterAgentType(Type agentType);

    /// <summary>
    /// Register an event type
    /// </summary>
    void RegisterEventType(Type eventType);

    /// <summary>
    /// Unregister an event type
    /// </summary>
    void UnregisterEventType(Type eventType);
}

/// <summary>
/// Agent metadata provider - type metadata and supported events
/// </summary>
public interface IAgentMetadataProvider
{
    /// <summary>
    /// Get metadata for an agent type
    /// </summary>
    AgentTypeMetadata? GetAgentMetadata(Type agentType);

    /// <summary>
    /// Get metadata for an agent type
    /// </summary>
    AgentTypeMetadata? GetAgentMetadata<TAgent>() where TAgent : IGAgent;

    /// <summary>
    /// Get all agent type metadata
    /// </summary>
    IReadOnlyList<AgentTypeMetadata> GetAllAgentMetadata();

    /// <summary>
    /// Get supported event types for an agent
    /// </summary>
    List<Type> GetSupportedEventTypes<TAgent>() where TAgent : IGAgent;

    /// <summary>
    /// Get supported event types for an agent
    /// </summary>
    List<Type> GetSupportedEventTypes(Type agentType);
}

/// <summary>
/// Agent plugin loader - assembly loading and unloading
/// </summary>
public interface IAgentPluginLoader
{
    /// <summary>
    /// Load agent types from assembly
    /// </summary>
    /// <param name="assembly">Assembly to load</param>
    /// <returns>Number of loaded agent types</returns>
    int LoadAgentTypesFromAssembly(System.Reflection.Assembly assembly);

    /// <summary>
    /// Load agent types from assembly path
    /// </summary>
    /// <param name="assemblyPath">Assembly file path</param>
    /// <returns>Number of loaded agent types</returns>
    int LoadAgentTypesFromPath(string assemblyPath);

    /// <summary>
    /// Unload all agent types from assembly
    /// </summary>
    void UnloadAgentTypesFromAssembly(System.Reflection.Assembly assembly);
}

#endregion

#region Aggregate Interface (backward compatible)

/// <summary>
/// Agent manager interface - aggregate of all agent management capabilities
/// Combines IAgentTypeRegistry, IAgentTypeRegistrar, IAgentMetadataProvider, IAgentPluginLoader
/// for backward compatibility
/// </summary>
public interface IGAgentManager :
    IAgentTypeRegistry,
    IAgentTypeRegistrar,
    IAgentMetadataProvider,
    IAgentPluginLoader
{
}

#endregion

#region Metadata Types

/// <summary>
/// Agent type metadata
/// </summary>
public record AgentTypeMetadata
{
    /// <summary>
    /// Agent type
    /// </summary>
    public Type AgentType { get; init; } = null!;

    /// <summary>
    /// Agent type name
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Agent description
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// State type
    /// </summary>
    public Type? StateType { get; init; }

    /// <summary>
    /// Supported event types
    /// </summary>
    public List<Type> SupportedEventTypes { get; init; } = new();

    /// <summary>
    /// Whether event sourcing is supported
    /// </summary>
    public bool SupportsEventSourcing { get; init; }

    /// <summary>
    /// Whether configuration is supported
    /// </summary>
    public bool SupportsConfiguration { get; init; }

    /// <summary>
    /// Source assembly name
    /// </summary>
    public string? AssemblyName { get; init; }

    /// <summary>
    /// Registration time
    /// </summary>
    public DateTimeOffset RegisteredAt { get; init; } = DateTimeOffset.UtcNow;
}

#endregion
