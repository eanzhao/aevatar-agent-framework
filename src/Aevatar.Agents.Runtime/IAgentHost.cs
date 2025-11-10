using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;

namespace Aevatar.Agents.Runtime;

/// <summary>
/// Represents a host environment for running agents.
/// </summary>
public interface IAgentHost
{
    /// <summary>
    /// Gets the unique identifier for this host.
    /// </summary>
    string HostId { get; }

    /// <summary>
    /// Gets the name of this host.
    /// </summary>
    string HostName { get; }

    /// <summary>
    /// Gets the runtime type this host is running on.
    /// </summary>
    string RuntimeType { get; }

    /// <summary>
    /// Gets the port number if applicable (for network-based runtimes).
    /// </summary>
    int? Port { get; }

    /// <summary>
    /// Registers an agent with the host.
    /// </summary>
    /// <param name="agentId">The unique identifier for the agent.</param>
    /// <param name="agent">The agent instance.</param>
    /// <returns>A task representing the registration operation.</returns>
    Task RegisterAgentAsync(string agentId, IAgentInstance agent);

    /// <summary>
    /// Unregisters an agent from the host.
    /// </summary>
    /// <param name="agentId">The unique identifier of the agent to unregister.</param>
    /// <returns>A task representing the unregistration operation.</returns>
    Task UnregisterAgentAsync(string agentId);

    /// <summary>
    /// Gets an agent by its identifier.
    /// </summary>
    /// <typeparam name="TAgent">The type of agent to retrieve.</typeparam>
    /// <param name="agentId">The unique identifier of the agent.</param>
    /// <returns>The agent instance if found; otherwise, null.</returns>
    Task<IAgentInstance?> GetAgentAsync(string agentId);

    /// <summary>
    /// Gets all registered agent identifiers.
    /// </summary>
    /// <returns>A collection of agent identifiers.</returns>
    Task<IReadOnlyList<string>> GetAgentIdsAsync();

    /// <summary>
    /// Checks if the host is healthy.
    /// </summary>
    /// <returns>True if the host is healthy; otherwise, false.</returns>
    Task<bool> IsHealthyAsync();

    /// <summary>
    /// Starts the host.
    /// </summary>
    /// <returns>A task representing the start operation.</returns>
    Task StartAsync();

    /// <summary>
    /// Stops the host.
    /// </summary>
    /// <returns>A task representing the stop operation.</returns>
    Task StopAsync();
}
