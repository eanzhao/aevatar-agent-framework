using System;
using System.Collections.Generic;

namespace Aevatar.Agents.Runtime;

/// <summary>
/// Metadata about an agent instance.
/// </summary>
public class AgentMetadata
{
    /// <summary>
    /// Gets or sets when the agent was activated.
    /// </summary>
    public DateTime ActivationTime { get; set; }

    /// <summary>
    /// Gets or sets the last activity time.
    /// </summary>
    public DateTime LastActivityTime { get; set; }

    /// <summary>
    /// Gets or sets the number of events processed.
    /// </summary>
    public long EventsProcessed { get; set; }

    /// <summary>
    /// Gets or sets whether the agent is currently active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets the parent agent ID if applicable.
    /// </summary>
    public Guid? ParentAgentId { get; set; }

    /// <summary>
    /// Gets or sets the collection of child agent IDs.
    /// </summary>
    public List<Guid> ChildAgentIds { get; set; } = new();

    /// <summary>
    /// Gets or sets custom tags associated with the agent.
    /// </summary>
    public Dictionary<string, string> Tags { get; set; } = new();
}
