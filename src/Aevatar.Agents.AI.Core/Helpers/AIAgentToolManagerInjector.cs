using System.Reflection;
using Aevatar.Agents.Abstractions;

namespace Aevatar.Agents.AI.Core.Helpers;

/// <summary>
/// Automatically injects IAevatarToolManager instances into agents that support tools.
/// </summary>
public static class AIAgentToolManagerInjector
{
    public static void InjectToolManager(IGAgent agent, IServiceProvider serviceProvider)
    {
        if (agent == null || serviceProvider == null)
            return;

        var property = FindToolManagerProperty(agent.GetType());
        if (property == null || !property.CanWrite)
            return;

        var toolManager = serviceProvider.GetService(property.PropertyType);
        if (toolManager == null)
            return;

        try
        {
            property.SetValue(agent, toolManager);
        }
        catch
        {
            // Swallow injection errors to avoid breaking agent creation.
        }
    }

    private static PropertyInfo? FindToolManagerProperty(Type agentType)
    {
        var currentType = agentType;
        while (currentType != null && currentType != typeof(object))
        {
            var property = currentType.GetProperty(
                "ToolManager",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            if (property != null && property.PropertyType.Name == "IAevatarToolManager")
            {
                return property;
            }

            currentType = currentType.BaseType;
        }

        return null;
    }
}

