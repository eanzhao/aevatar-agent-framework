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
        var baseType = agentType.BaseType;

        // Find GAgentBaseWithEventSourcing<TState> in the inheritance chain
        while (baseType != null && baseType != typeof(object))
        {
            if (baseType.IsGenericType &&
                baseType.GetGenericTypeDefinition().Name == "GAgentBaseWithEventSourcing`1")
            {
                // Get IEventStore from DI
                var eventStore = serviceProvider.GetService(typeof(IEventStore));
                if (eventStore != null)
                {
                    // Find and call SetEventStore method
                    var setEventStoreMethod = FindSetEventStoreMethod(agentType);
                    if (setEventStoreMethod != null)
                    {
                        try
                        {
                            setEventStoreMethod.Invoke(agent, [eventStore]);
                        }
                        catch (Exception)
                        {
                            // Log error silently (Agent may work without EventStore)
                        }
                    }
                }

                break;
            }

            baseType = baseType.BaseType;
        }
    }

    /// <summary>
    /// Find SetEventStore method in agent type hierarchy
    /// </summary>
    private static MethodInfo? FindSetEventStoreMethod(Type agentType)
    {
        const BindingFlags bindingFlags =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic;

        // Check current type
        var methods = agentType.GetMethods(bindingFlags);
        foreach (var method in methods)
        {
            if (method.Name == "SetEventStore" &&
                method.ReturnType == typeof(void) &&
                method.GetParameters().Length == 1 &&
                method.GetParameters()[0].ParameterType == typeof(IEventStore))
            {
                return method;
            }
        }

        // Check base type recursively
        if (agentType.BaseType != null && agentType.BaseType != typeof(object))
        {
            var baseMethod = FindSetEventStoreMethod(agentType.BaseType);
            if (baseMethod != null)
                return baseMethod;
        }

        return null;
    }
}
