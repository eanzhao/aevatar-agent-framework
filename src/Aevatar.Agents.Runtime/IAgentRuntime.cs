using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;

namespace Aevatar.Agents.Runtime;

/// <summary>
/// Defines the contract for agent runtime implementations.
/// Provides abstraction over different execution environments (Local, ProtoActor, Orleans).
/// </summary>
public interface IAgentRuntime
{
    /// <summary>
    /// Gets the type of runtime (e.g., "Local", "ProtoActor", "Orleans").
    /// </summary>
    string RuntimeType { get; }

    /// <summary>
    /// Creates and initializes a host for running agents.
    /// </summary>
    /// <param name="config">The configuration for the agent host.</param>
    /// <returns>An initialized agent host.</returns>
    Task<IAgentHost> CreateHostAsync(AgentHostConfiguration config);

    /// <summary>
    /// Spawns a new agent instance in the runtime.
    /// </summary>
    /// <typeparam name="TAgent">The type of agent to spawn.</typeparam>
    /// <param name="options">Options for spawning the agent.</param>
    /// <returns>The spawned agent instance.</returns>
    Task<IAgentInstance> SpawnAgentAsync<TAgent>(AgentSpawnOptions options) 
        where TAgent : class, new();

    /// <summary>
    /// Checks if the runtime is healthy and operational.
    /// </summary>
    /// <returns>True if the runtime is healthy; otherwise, false.</returns>
    Task<bool> IsHealthyAsync();

    /// <summary>
    /// Gracefully shuts down the runtime.
    /// </summary>
    /// <returns>A task representing the shutdown operation.</returns>
    Task ShutdownAsync();
}
