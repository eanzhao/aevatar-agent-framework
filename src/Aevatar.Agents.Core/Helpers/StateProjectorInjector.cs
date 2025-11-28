using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.CQRS;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.Core.Helpers;

/// <summary>
/// Automatic StateProjector injector for Agent
/// Injects IStateProjector after Agent instance creation for CQRS state projection
/// </summary>
public static class StateProjectorInjector
{
    /// <summary>
    /// Inject StateProjector for Agent
    /// </summary>
    /// <param name="agent">Agent instance</param>
    /// <param name="serviceProvider">Service provider</param>
    public static void InjectStateProjector(IGAgent? agent, IServiceProvider? serviceProvider)
    {
        if (agent == null || serviceProvider == null)
            return;

        var agentType = agent.GetType();

        // Try to get IStateProjector from service container
        var stateProjector = serviceProvider.GetService<IStateProjector>();

        if (stateProjector == null)
            return;

        // Find StateProjector property
        var property = FindStateProjectorProperty(agentType);

        if (property != null && property.CanWrite)
        {
            try
            {
                // Check if already set
                var currentValue = property.GetValue(agent);
                if (currentValue != null)
                    return;

                // Inject StateProjector
                property.SetValue(agent, stateProjector);
            }
            catch
            {
                // Ignore injection failure - Agent can work without CQRS projection
            }
        }
    }

    /// <summary>
    /// Find StateProjector property in type hierarchy
    /// </summary>
    private static PropertyInfo? FindStateProjectorProperty(Type type)
    {
        const BindingFlags bindingFlags =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic;

        // Find IStateProjector type property named StateProjector
        var property = type.GetProperty("StateProjector", bindingFlags);

        if (property != null && typeof(IStateProjector).IsAssignableFrom(property.PropertyType))
        {
            return property;
        }

        // Recursively find in base class
        if (type.BaseType != null && type.BaseType != typeof(object))
        {
            return FindStateProjectorProperty(type.BaseType);
        }

        return null;
    }
}
