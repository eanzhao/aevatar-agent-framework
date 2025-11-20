using System.Reflection;
using Aevatar.Agents.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Agents.Core.Helpers;

/// <summary>
/// Agent 和 Actor Logger 自动注入器
/// 在创建 Agent 或 Actor 实例后自动注入 Logger
/// </summary>
public static class LoggerInjector
{
    /// <summary>
    /// 为 Agent 注入 Logger
    /// </summary>
    /// <param name="agent">Agent 实例</param>
    /// <param name="serviceProvider">服务提供者</param>
    public static void InjectLogger(IGAgent? agent, IServiceProvider serviceProvider)
    {
        if (agent == null)
            return;
            
        var agentType = agent.GetType();
        
        // 尝试从服务容器获取 ILoggerFactory
        var loggerFactory = serviceProvider?.GetService<ILoggerFactory>();
        
        if (loggerFactory == null)
            return;
            
        // 创建针对 Agent 类型的 Logger
        var logger = loggerFactory.CreateLogger(agentType) ?? NullLogger.Instance;
        
        // 查找 Logger 属性
        var loggerProperty = FindLoggerProperty(agentType);
        
        if (loggerProperty != null && loggerProperty.CanWrite)
        {
            try
            {
                // 检查当前值是否已经是非 NullLogger
                var currentValue = loggerProperty.GetValue(agent);
                if (currentValue is ILogger currentLogger && 
                    currentLogger.GetType() != typeof(NullLogger) &&
                    currentLogger.GetType() != typeof(NullLogger<>))
                {
                    // 已经有有效的 Logger，不覆盖
                    return;
                }
                
                // 注入新的 Logger
                loggerProperty.SetValue(agent, logger);
            }
            catch
            {
                // 忽略注入失败，Agent 仍可使用默认 Logger
            }
        }
    }
    
    /// <summary>
    /// 查找 Logger 属性
    /// 支持 protected 和 public 属性
    /// </summary>
    private static PropertyInfo? FindLoggerProperty(Type type)
    {
        const BindingFlags bindingFlags = 
            BindingFlags.Instance | 
            BindingFlags.Public | 
            BindingFlags.NonPublic;
            
        // 查找名为 Logger 的 ILogger 类型属性
        var property = type.GetProperty("Logger", bindingFlags);
        
        if (property != null && typeof(ILogger).IsAssignableFrom(property.PropertyType))
        {
            return property;
        }
        
        // 在基类中递归查找
        if (type.BaseType != null && type.BaseType != typeof(object))
        {
            return FindLoggerProperty(type.BaseType);
        }
        
        return null;
    }
    
    /// <summary>
    /// 创建并注入 Logger
    /// 便捷方法，用于在已有 Logger 实例时直接注入
    /// </summary>
    /// <param name="agent">Agent 实例</param>
    /// <param name="logger">Logger 实例</param>
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
                // 忽略注入失败
            }
        }
    }

    /// <summary>
    /// 为 Actor 注入 Logger
    /// </summary>
    /// <param name="actor">Actor 实例</param>
    /// <param name="serviceProvider">服务提供者</param>
    public static void InjectLogger(IGAgentActor actor, IServiceProvider serviceProvider)
    {
        if (actor == null)
            return;

        var actorType = actor.GetType();

        // 尝试从服务容器获取 ILoggerFactory
        var loggerFactory = serviceProvider?.GetService<ILoggerFactory>();

        if (loggerFactory == null)
            return;

        // 创建针对 Actor 类型的 Logger
        var logger = loggerFactory.CreateLogger(actorType) ?? NullLogger.Instance;

        // 查找 Logger 属性
        var loggerProperty = FindLoggerProperty(actorType);

        if (loggerProperty != null && loggerProperty.CanWrite)
        {
            try
            {
                // 检查当前值是否已经是非 NullLogger
                var currentValue = loggerProperty.GetValue(actor);
                if (currentValue is ILogger currentLogger &&
                    currentLogger.GetType() != typeof(NullLogger) &&
                    currentLogger.GetType() != typeof(NullLogger<>))
                {
                    // 已经有有效的 Logger，不覆盖
                    return;
                }

                // 注入新的 Logger
                loggerProperty.SetValue(actor, logger);
            }
            catch
            {
                // 忽略注入失败，Actor 仍可使用默认 Logger
            }
        }
    }

    /// <summary>
    /// 直接注入 Logger 到 Actor
    /// </summary>
    /// <param name="actor">Actor 实例</param>
    /// <param name="logger">Logger 实例</param>
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
                // 忽略注入失败
            }
        }
    }

    /// <summary>
    /// 通用的 Logger 注入方法
    /// 支持任何具有 Logger 属性的对象
    /// </summary>
    /// <param name="target">目标对象</param>
    /// <param name="serviceProvider">服务提供者</param>
    public static void InjectLogger(object target, IServiceProvider serviceProvider)
    {
        if (target == null)
            return;

        var targetType = target.GetType();

        // 尝试从服务容器获取 ILoggerFactory
        var loggerFactory = serviceProvider?.GetService<ILoggerFactory>();

        if (loggerFactory == null)
            return;

        // 创建针对目标类型的 Logger
        var logger = loggerFactory.CreateLogger(targetType) ?? NullLogger.Instance;

        // 查找 Logger 属性
        var loggerProperty = FindLoggerProperty(targetType);

        if (loggerProperty != null && loggerProperty.CanWrite)
        {
            try
            {
                // 检查当前值是否已经是非 NullLogger
                var currentValue = loggerProperty.GetValue(target);
                if (currentValue is ILogger currentLogger &&
                    currentLogger.GetType() != typeof(NullLogger) &&
                    currentLogger.GetType() != typeof(NullLogger<>))
                {
                    // 已经有有效的 Logger，不覆盖
                    return;
                }

                // 注入新的 Logger
                loggerProperty.SetValue(target, logger);
            }
            catch
            {
                // 忽略注入失败
            }
        }
    }
}
