using System.Threading.Tasks;

namespace Aevatar.Agents.Runtime;

/// <summary>
/// Factory interface for creating agent runtimes.
/// </summary>
public interface IAgentRuntimeFactory
{
    /// <summary>
    /// Creates an agent runtime of the specified type.
    /// </summary>
    /// <param name="runtimeType">The type of runtime to create.</param>
    /// <returns>The created agent runtime.</returns>
    IAgentRuntime CreateRuntime(string runtimeType);
    
    /// <summary>
    /// Gets an existing runtime or creates a new one if it doesn't exist.
    /// </summary>
    /// <param name="runtimeType">The type of runtime.</param>
    /// <returns>The agent runtime.</returns>
    IAgentRuntime GetOrCreateRuntime(string runtimeType);
}
