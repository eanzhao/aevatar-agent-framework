using System.Reflection;
using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core;

/// <summary>
/// Agent 基类（带配置支持）
/// 提供动态配置机制
/// </summary>
/// <typeparam name="TState">Agent 状态类型</typeparam>
/// <typeparam name="TEvent">事件基类型</typeparam>
/// <typeparam name="TConfiguration">配置类型（必须是 IMessage）</typeparam>
public abstract class GAgentBase<TState, TEvent, TConfiguration> : GAgentBase<TState, TEvent>
    where TState : class, new()
    where TEvent : class, IMessage
    where TConfiguration : class, IMessage
{
    protected GAgentBase(ILogger? logger = null) : base(logger)
    {
    }
    
    protected GAgentBase(Guid id, ILogger? logger = null) : base(id, logger)
    {
    }
    
    /// <summary>
    /// 配置 Agent
    /// </summary>
    /// <param name="configuration">配置对象</param>
    /// <param name="ct">取消令牌</param>
    public virtual async Task ConfigureAsync(TConfiguration configuration, CancellationToken ct = default)
    {
        _logger.LogInformation("Configuring agent {Id} with {ConfigType}", Id, typeof(TConfiguration).Name);
        
        await OnConfigureAsync(configuration, ct);
        
        // 发布配置事件（可选）
        // await PublishAsync(configuration, EventDirection.Down, ct);
    }
    
    /// <summary>
    /// 配置回调（由子类重写）
    /// </summary>
    /// <param name="configuration">配置对象</param>
    /// <param name="ct">取消令牌</param>
    protected virtual Task OnConfigureAsync(TConfiguration configuration, CancellationToken ct = default)
    {
        // 默认实现：什么都不做
        // 子类可以重写此方法来处理配置
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// 获取配置类型
    /// </summary>
    public virtual Type GetConfigurationType()
    {
        return typeof(TConfiguration);
    }
    
    /// <summary>
    /// 判断方法是否是配置处理器
    /// </summary>
    protected override bool IsEventHandlerMethod(MethodInfo method)
    {
        // 先检查基类的事件处理器
        if (base.IsEventHandlerMethod(method))
            return true;
        
        // 检查是否是配置处理器
        var parameters = method.GetParameters();
        if (parameters.Length != 1) return false;
        
        var paramType = parameters[0].ParameterType;
        
        // [Configuration] 标记的方法，参数必须是 TConfiguration
        if (method.GetCustomAttribute<ConfigurationAttribute>() != null)
        {
            return typeof(TConfiguration).IsAssignableFrom(paramType);
        }
        
        // 默认配置处理器：方法名为 HandleConfigAsync 或 OnConfigureAsync
        if (method.Name is "HandleConfigAsync" or "OnConfigureAsync")
        {
            return typeof(TConfiguration).IsAssignableFrom(paramType);
        }
        
        return false;
    }
}

