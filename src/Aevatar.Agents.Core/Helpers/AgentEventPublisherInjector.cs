using System.Reflection;
using Aevatar.Agents.Abstractions;

namespace Aevatar.Agents.Core.Helpers;

/// <summary>
/// Agent EventPublisher automatic injector
/// Injects IEventPublisher into Agent instances after creation
/// </summary>
public static class AgentEventPublisherInjector
{
    /// <summary>
    /// Inject EventPublisher into Agent
    /// </summary>
    /// <param name="agent">Agent instance</param>
    /// <param name="eventPublisher">Event publisher to inject</param>
    public static void InjectEventPublisher(IGAgent agent, IEventPublisher eventPublisher)
    {
        if (agent == null || eventPublisher == null)
            return;

        var agentType = agent.GetType();

        // Find EventPublisher field in the agent hierarchy
        var eventPublisherField = FindEventPublisherField(agentType);
        if (eventPublisherField != null)
        {
            try
            {
                eventPublisherField.SetValue(agent, eventPublisher);
            }
            catch (Exception)
            {
                // Log error silently (Agent may work without EventPublisher in tests)
            }
        }
    }

    /// <summary>
    /// Find EventPublisher field in agent type hierarchy
    /// </summary>
    private static FieldInfo? FindEventPublisherField(Type agentType)
    {
        const BindingFlags bindingFlags =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic;

        // Check current type and all base types
        var currentType = agentType;
        while (currentType != null && currentType != typeof(object))
        {
            var field = currentType.GetField("EventPublisher", bindingFlags);
            if (field != null && field.FieldType == typeof(IEventPublisher))
            {
                return field;
            }

            currentType = currentType.BaseType;
        }

        return null;
    }

    /// <summary>
    /// Check if agent has EventPublisher field
    /// </summary>
    /// <param name="agent">Agent instance</param>
    /// <returns>True if agent has EventPublisher field</returns>
    public static bool HasEventPublisher(IGAgent agent)
    {
        if (agent == null)
            return false;

        var agentType = agent.GetType();
        var field = FindEventPublisherField(agentType);
        return field != null;
    }
}