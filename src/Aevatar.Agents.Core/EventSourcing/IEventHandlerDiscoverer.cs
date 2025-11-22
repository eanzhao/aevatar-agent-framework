using System.Reflection;

namespace Aevatar.Agents.Core.EventSourcing;

/// <summary>
/// Interface for discovering event handler methods on agent types.
/// </summary>
public interface IEventHandlerDiscoverer
{
    /// <summary>
    /// Discover event handler methods for the specified type.
    /// </summary>
    /// <param name="type">The agent type to analyze.</param>
    /// <returns>Array of MethodInfo representing event handlers.</returns>
    MethodInfo[] DiscoverEventHandlers(Type type);
}
