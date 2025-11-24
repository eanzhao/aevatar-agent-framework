using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.EventRouting;

namespace Aevatar.Agents.Core.Helpers;

/// <summary>
/// Automatic EventRouterFactory injector for GAgentActorBase
/// Automatically inject EventRouterFactory after creating Actor instance
/// </summary>
public static class EventRouterFactoryInjector
{
    /// <summary>
    /// Inject EventRouterFactory for Actor
    /// </summary>
    /// <param name="actor">Actor instance</param>
    /// <param name="serviceProvider">Service provider</param>
    public static void InjectEventRouterFactory(IGAgentActor? actor, IServiceProvider? serviceProvider)
    {
        if (actor == null || serviceProvider == null)
            return;

        var actorType = actor.GetType();

        // Find EventRouterFactory field
        var factoryField = FindEventRouterFactoryField(actorType);

        if (factoryField != null)
        {
            try
            {
                // Create EventRouterFactory with serviceProvider
                var factory = new EventRouterFactory(serviceProvider);

                // Inject factory
                factoryField.SetValue(actor, factory);
            }
            catch
            {
                // Ignore injection failure
            }
        }
    }

    /// <summary>
    /// Find EventRouterFactory field
    /// Supports protected and private fields
    /// </summary>
    private static FieldInfo? FindEventRouterFactoryField(Type type)
    {
        const BindingFlags bindingFlags =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic;

        // Find EventRouterFactory type field
        var fields = type.GetFields(bindingFlags);
        foreach (var field in fields)
        {
            if (field.FieldType == typeof(EventRouterFactory))
            {
                return field;
            }
        }

        // Recursively find in base class
        if (type.BaseType != null && type.BaseType != typeof(object))
        {
            return FindEventRouterFactoryField(type.BaseType);
        }

        return null;
    }
}