using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Persistence;

namespace Aevatar.Agents.Core.Helpers;

/// <summary>
/// Agent Configuration automatic injector
/// Injects IConfigurationStore into Agent instances after creation
/// </summary>
public static class AgentConfigurationInjector
{
    /// <summary>
    /// Find ConfigurationStore property in agent type hierarchy
    /// </summary>
    private static PropertyInfo? FindConfigurationStoreProperty(Type agentType, Type configType)
    {
        const BindingFlags bindingFlags =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic;

        var properties = agentType.GetProperties(bindingFlags);
        foreach (var prop in properties)
        {
            if (prop.Name == "ConfigStore" &&
                prop.PropertyType.IsGenericType &&
                prop.PropertyType.GetGenericTypeDefinition() == typeof(IConfigurationStore<>))
            {
                return prop;
            }
        }

        if (agentType.BaseType != null && agentType.BaseType != typeof(object))
        {
            return FindConfigurationStoreProperty(agentType.BaseType, configType);
        }

        return null;
    }

    /// <summary>
    /// Inject ConfigurationStore into Agent
    /// </summary>
    /// <param name="agent">Agent instance</param>
    /// <param name="serviceProvider">Service provider</param>
    public static void InjectConfigurationStore(IGAgent agent, IServiceProvider serviceProvider)
    {
        if (agent == null || serviceProvider == null)
            return;

        var agentType = agent.GetType();
        var baseType = agentType.BaseType;

        while (baseType != null && baseType != typeof(object))
        {
            if (baseType.IsGenericType &&
                baseType.GetGenericTypeDefinition() == typeof(GAgentBase<,>))
            {
                var genericArgs = baseType.GetGenericArguments();
                if (genericArgs.Length == 2)
                {
                    var configType = genericArgs[1];
                    var configStoreType = typeof(IConfigurationStore<>).MakeGenericType(configType);

                    var configStore = serviceProvider.GetService(configStoreType);
                    if (configStore != null)
                    {
                        var configStoreProperty = FindConfigurationStoreProperty(agentType, configType);
                        if (configStoreProperty != null && configStoreProperty.CanWrite)
                        {
                            try
                            {
                                configStoreProperty.SetValue(agent, configStore);
                            }
                            catch (Exception)
                            {
                                // Silently handle injection failure
                            }
                        }
                    }
                }
                break;
            }

            baseType = baseType.BaseType;
        }
    }
}
