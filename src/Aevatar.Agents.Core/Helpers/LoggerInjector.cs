using System.Reflection;
using Aevatar.Agents.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Agents.Core.Helpers;

/// <summary>
/// Automatic Logger injector for Agent and Actor
/// Automatically inject Logger after creating Agent or Actor instance
/// </summary>
public static class LoggerInjector
{
    /// <summary>
    /// Inject Logger for Agent
    /// </summary>
    /// <param name="agent">Agent instance</param>
    /// <param name="serviceProvider">Service provider</param>
    public static void InjectLogger(IGAgent? agent, IServiceProvider serviceProvider)
    {
        if (agent == null)
            return;
            
        var agentType = agent.GetType();
        
        // Try to get ILoggerFactory from service container
        var loggerFactory = serviceProvider?.GetService<ILoggerFactory>();
        
        if (loggerFactory == null)
            return;
            
        // Create Logger for Agent type
        var logger = loggerFactory.CreateLogger(agentType) ?? NullLogger.Instance;
        
        // Find Logger property
        var loggerProperty = FindLoggerProperty(agentType);
        
        if (loggerProperty != null && loggerProperty.CanWrite)
        {
            try
            {
                // Check if current value is already a non-NullLogger
                var currentValue = loggerProperty.GetValue(agent);
                if (currentValue is ILogger currentLogger && 
                    currentLogger.GetType() != typeof(NullLogger) &&
                    currentLogger.GetType() != typeof(NullLogger<>))
                {
                    // Already has a valid Logger, don't overwrite
                    return;
                }
                
                // Inject new Logger
                loggerProperty.SetValue(agent, logger);
            }
            catch
            {
                // Ignore injection failure, Agent can still use default Logger
            }
        }
    }
    
    /// <summary>
    /// Find Logger property
    /// Supports protected and public properties
    /// </summary>
    private static PropertyInfo? FindLoggerProperty(Type type)
    {
        const BindingFlags bindingFlags = 
            BindingFlags.Instance | 
            BindingFlags.Public | 
            BindingFlags.NonPublic;
            
        // Find ILogger type property named Logger
        var property = type.GetProperty("Logger", bindingFlags);
        
        if (property != null && typeof(ILogger).IsAssignableFrom(property.PropertyType))
        {
            return property;
        }
        
        // Recursively find in base class
        if (type.BaseType != null && type.BaseType != typeof(object))
        {
            return FindLoggerProperty(type.BaseType);
        }
        
        return null;
    }
    
    /// <summary>
    /// Create and inject Logger
    /// Convenience method for direct injection when Logger instance already exists
    /// </summary>
    /// <param name="agent">Agent instance</param>
    /// <param name="logger">Logger instance</param>
    public static void InjectLogger(IGAgent agent, ILogger logger)
    {
        if (agent == null || logger == null)
            return;

        var agentType = agent.GetType();
        var loggerProperty = FindLoggerProperty(agentType);

        if (loggerProperty != null && loggerProperty.CanWrite)
        {
            try
            {
                loggerProperty.SetValue(agent, logger);
            }
            catch
            {
                // Ignore injection failure
            }
        }
    }

    /// <summary>
    /// Inject Logger for Actor
    /// </summary>
    /// <param name="actor">Actor instance</param>
    /// <param name="serviceProvider">Service provider</param>
    public static void InjectLogger(IGAgentActor actor, IServiceProvider serviceProvider)
    {
        if (actor == null)
            return;

        var actorType = actor.GetType();

        // Try to get ILoggerFactory from service container
        var loggerFactory = serviceProvider?.GetService<ILoggerFactory>();

        if (loggerFactory == null)
            return;

        // Create Logger for Actor type
        var logger = loggerFactory.CreateLogger(actorType) ?? NullLogger.Instance;

        // Find Logger property
        var loggerProperty = FindLoggerProperty(actorType);

        if (loggerProperty != null && loggerProperty.CanWrite)
        {
            try
            {
                // Check if current value is already a non-NullLogger
                var currentValue = loggerProperty.GetValue(actor);
                if (currentValue is ILogger currentLogger &&
                    currentLogger.GetType() != typeof(NullLogger) &&
                    currentLogger.GetType() != typeof(NullLogger<>))
                {
                    // Already has a valid Logger, don't overwrite
                    return;
                }

                // Inject new Logger
                loggerProperty.SetValue(actor, logger);
            }
            catch
            {
                // Ignore injection failure, Actor can still use default Logger
            }
        }
    }

    /// <summary>
    /// Directly inject Logger to Actor
    /// </summary>
    /// <param name="actor">Actor instance</param>
    /// <param name="logger">Logger instance</param>
    public static void InjectLogger(IGAgentActor actor, ILogger logger)
    {
        if (actor == null || logger == null)
            return;

        var actorType = actor.GetType();
        var loggerProperty = FindLoggerProperty(actorType);

        if (loggerProperty != null && loggerProperty.CanWrite)
        {
            try
            {
                loggerProperty.SetValue(actor, logger);
            }
            catch
            {
                // Ignore injection failure
            }
        }
    }

    /// <summary>
    /// Generic Logger injection method
    /// Supports any object with Logger property
    /// </summary>
    /// <param name="target">Target object</param>
    /// <param name="serviceProvider">Service provider</param>
    public static void InjectLogger(object target, IServiceProvider serviceProvider)
    {
        if (target == null)
            return;

        var targetType = target.GetType();

        // Try to get ILoggerFactory from service container
        var loggerFactory = serviceProvider?.GetService<ILoggerFactory>();

        if (loggerFactory == null)
            return;

        // Create Logger for target type
        var logger = loggerFactory.CreateLogger(targetType) ?? NullLogger.Instance;

        // Find Logger property
        var loggerProperty = FindLoggerProperty(targetType);

        if (loggerProperty != null && loggerProperty.CanWrite)
        {
            try
            {
                // Check if current value is already a non-NullLogger
                var currentValue = loggerProperty.GetValue(target);
                if (currentValue is ILogger currentLogger &&
                    currentLogger.GetType() != typeof(NullLogger) &&
                    currentLogger.GetType() != typeof(NullLogger<>))
                {
                    // Already has a valid Logger, don't overwrite
                    return;
                }

                // Inject new Logger
                loggerProperty.SetValue(target, logger);
            }
            catch
            {
                // Ignore injection failure
            }
        }
    }
}
