using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Persistence;

namespace Aevatar.Agents.Core.Helpers;

/// <summary>
/// Agent StateStore automatic injector
/// Injects IStateStore into Agent instances after creation
/// </summary>
public static class AgentStateStoreInjector
{
    /// <summary>
    /// Inject StateStore into Agent
    /// </summary>
    /// <param name="agent">Agent instance</param>
    /// <param name="serviceProvider">Service provider</param>
    public static void InjectStateStore(IGAgent agent, IServiceProvider serviceProvider)
    {
        if (agent == null || serviceProvider == null)
            return;

        var agentType = agent.GetType();
        var baseType = agentType.BaseType;

        // Find GAgentBase<TState> in the inheritance chain
        while (baseType != null && baseType != typeof(object))
        {
            if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(GAgentBase<>))
            {
                var stateType = baseType.GetGenericArguments()[0];
                var stateStoreType = typeof(IStateStore<>).MakeGenericType(stateType);

                // Get StateStore from DI
                var stateStore = serviceProvider.GetService(stateStoreType);
                if (stateStore != null)
                {
                    // Find StateStore property in the agent hierarchy
                    var stateStoreProperty = FindStateStoreProperty(agentType, stateType);
                    if (stateStoreProperty != null && stateStoreProperty.CanWrite)
                    {
                        try
                        {
                            stateStoreProperty.SetValue(agent, stateStore);
                        }
                        catch (Exception)
                        {
                            // Log error silently (Agent may work without StateStore)
                        }
                    }
                }

                break;
            }

            baseType = baseType.BaseType;
        }
    }

    /// <summary>
    /// Find StateStore property in agent type hierarchy
    /// </summary>
    private static PropertyInfo? FindStateStoreProperty(Type agentType, Type stateType)
    {
        const BindingFlags bindingFlags =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic;

        // Check current type
        var properties = agentType.GetProperties(bindingFlags);
        foreach (var prop in properties)
        {
            if (prop.Name == "StateStore" &&
                prop.PropertyType.IsGenericType &&
                prop.PropertyType.GetGenericTypeDefinition() == typeof(IStateStore<>))
            {
                return prop;
            }
        }

        // Check base type recursively
        if (agentType.BaseType != null && agentType.BaseType != typeof(object))
        {
            var baseProp = FindStateStoreProperty(agentType.BaseType, stateType);
            if (baseProp != null)
                return baseProp;
        }

        return null;
    }
}
