using System;
using System.Collections.Generic;
using Google.Protobuf;

namespace Aevatar.Agents.Runtime;

/// <summary>
/// Options for spawning an agent.
/// </summary>
public class AgentSpawnOptions
{
    /// <summary>
    /// Gets or sets the unique identifier for the agent.
    /// If not provided, a new GUID will be generated.
    /// </summary>
    public string? AgentId { get; set; }

    /// <summary>
    /// Gets or sets the parent agent ID if this is a child agent.
    /// </summary>
    public string? ParentAgentId { get; set; }

    /// <summary>
    /// Gets or sets whether to enable persistence for this agent.
    /// </summary>
    public bool EnablePersistence { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to enable streaming for this agent.
    /// </summary>
    public bool EnableStreaming { get; set; } = true;

    /// <summary>
    /// Gets or sets the initial state for the agent.
    /// Must be a Protobuf message type.
    /// </summary>
    public IMessage? InitialState { get; set; }

    /// <summary>
    /// Gets or sets agent-specific configuration.
    /// </summary>
    public Dictionary<string, object> Configuration { get; set; } = new();

    /// <summary>
    /// Gets or sets metadata tags for the agent.
    /// </summary>
    public Dictionary<string, string> Tags { get; set; } = new();

    /// <summary>
    /// Gets or sets the activation timeout in seconds.
    /// </summary>
    public int ActivationTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the deactivation timeout in seconds (for virtual actors).
    /// </summary>
    public int? DeactivationTimeoutSeconds { get; set; }

    /// <summary>
    /// Gets or sets whether to automatically subscribe to parent stream if ParentAgentId is set.
    /// </summary>
    public bool AutoSubscribeToParent { get; set; } = true;

    /// <summary>
    /// Gets or sets the placement strategy for distributed runtimes.
    /// </summary>
    public PlacementStrategy? Placement { get; set; }

    /// <summary>
    /// Gets or sets resource limits for the agent.
    /// </summary>
    public ResourceLimits? ResourceLimits { get; set; }
}

/// <summary>
/// Placement strategy for agent deployment in distributed runtimes.
/// </summary>
public class PlacementStrategy
{
    /// <summary>
    /// Gets or sets the placement type (e.g., "Random", "Local", "PreferLocal", "Custom").
    /// </summary>
    public string Type { get; set; } = "Random";

    /// <summary>
    /// Gets or sets the preferred node or silo for placement.
    /// </summary>
    public string? PreferredNode { get; set; }

    /// <summary>
    /// Gets or sets custom placement hints.
    /// </summary>
    public Dictionary<string, object> Hints { get; set; } = new();
}

/// <summary>
/// Resource limits for an agent.
/// </summary>
public class ResourceLimits
{
    /// <summary>
    /// Gets or sets the maximum memory in megabytes.
    /// </summary>
    public int? MaxMemoryMB { get; set; }

    /// <summary>
    /// Gets or sets the maximum CPU percentage (0-100).
    /// </summary>
    public int? MaxCpuPercent { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of concurrent events to process.
    /// </summary>
    public int? MaxConcurrentEvents { get; set; } = 100;

    /// <summary>
    /// Gets or sets the maximum event queue size.
    /// </summary>
    public int? MaxEventQueueSize { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the maximum state size in kilobytes.
    /// </summary>
    public int? MaxStateSizeKB { get; set; }
}
