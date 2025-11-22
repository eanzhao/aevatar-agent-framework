using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;

namespace Aevatar.Agents.Core.Helpers;

/// <summary>
/// Agent EventStore automatic injector
/// Injects IEventStore into Agent instances after creation
/// </summary>
public static class AgentEventStoreInjector
{
    /// <summary>
    /// Inject EventStore into Agent
    /// </summary>
    /// <param name="agent">Agent instance</param>
    /// <param name="serviceProvider">Service provider</param>
    public static void InjectEventStore(IGAgent agent, IServiceProvider serviceProvider)
    {
        if (agent == null || serviceProvider == null)
            return;

        var agentType = agent.GetType();

        // Find EventStore property (it's protected in GAgentBaseWithEventSourcing)
        var eventStoreProperty = agentType.GetProperty("EventStore",
            BindingFlags.Instance |
            BindingFlags.NonPublic |
            BindingFlags.Public);

        if (eventStoreProperty != null &&
            eventStoreProperty.PropertyType == typeof(IEventStore) &&
            eventStoreProperty.CanWrite)
        {
            // Get IEventStore from DI
            var eventStore = serviceProvider.GetService(typeof(IEventStore));
            if (eventStore != null)
            {
                try
                {
                    eventStoreProperty.SetValue(agent, eventStore);
                }
                catch (Exception)
                {
                    // Log error silently (Agent may work without EventStore)
                }
            }
        }
    }


    /// <summary>
    /// Check if agent needs EventStore
    /// </summary>
    /// <param name="agent">Agent instance</param>
    /// <returns>True if the agent has an EventStore property</returns>
    public static bool HasEventStore(IGAgent? agent)
    {
        if (agent == null)
            return false;

        var agentType = agent.GetType();

        // Check if agent has EventStore property (it's protected in GAgentBaseWithEventSourcing)
        var eventStoreProperty = agentType.GetProperty("EventStore",
            BindingFlags.Instance |
            BindingFlags.NonPublic |
            BindingFlags.Public);

        return eventStoreProperty != null &&
               eventStoreProperty.PropertyType == typeof(IEventStore) &&
               eventStoreProperty.CanWrite;
    }
}