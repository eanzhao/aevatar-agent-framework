using System;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Google.Protobuf;

namespace Aevatar.Agents.Runtime;

/// <summary>
/// Runtime representation of an agent instance.
/// This interface provides a non-generic way to interact with agents in the runtime.
/// </summary>
public interface IAgentInstance
{
    /// <summary>
    /// Gets the unique identifier for this agent instance.
    /// </summary>
    Guid AgentId { get; }

    /// <summary>
    /// Gets the runtime-specific identifier (e.g., grain ID for Orleans, PID for ProtoActor).
    /// </summary>
    string RuntimeId { get; }

    /// <summary>
    /// Gets the type name of the agent.
    /// </summary>
    string AgentTypeName { get; }

    /// <summary>
    /// Initializes the agent instance.
    /// </summary>
    /// <returns>A task representing the initialization operation.</returns>
    Task InitializeAsync();

    /// <summary>
    /// Publishes an event to the agent for processing.
    /// </summary>
    /// <param name="envelope">The event envelope to process.</param>
    /// <returns>A task representing the event processing operation.</returns>
    Task PublishEventAsync(EventEnvelope envelope);

    /// <summary>
    /// Gets the agent's current state as a protobuf message.
    /// </summary>
    /// <returns>The agent's state.</returns>
    Task<IMessage?> GetStateAsync();

    /// <summary>
    /// Sets the agent's state from a protobuf message.
    /// </summary>
    /// <param name="state">The state to set.</param>
    /// <returns>A task representing the state update operation.</returns>
    Task SetStateAsync(IMessage state);

    /// <summary>
    /// Deactivates the agent instance.
    /// </summary>
    /// <returns>A task representing the deactivation operation.</returns>
    Task DeactivateAsync();

    /// <summary>
    /// Gets metadata about the agent instance.
    /// </summary>
    /// <returns>Agent metadata.</returns>
    Task<AgentMetadata> GetMetadataAsync();
}
