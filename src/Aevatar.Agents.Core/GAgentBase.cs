using System.Collections.Concurrent;
using System.Reflection;
using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Agents.Core;

/// <summary>
/// Agent 基类（基础版本）
/// 提供事件处理器自动发现和调用机制
/// </summary>
/// <typeparam name="TState">Agent 状态类型</typeparam>
public abstract class GAgentBase<TState> : IGAgent<TState>
    where TState : class, new()
{
    // ============ 字段 ============
    
    protected readonly TState _state = new();
    protected readonly ILogger _logger;
    protected IEventPublisher? _eventPublisher;
    
    // 事件处理器缓存（类型 -> 方法列表）
    private static readonly ConcurrentDictionary<Type, MethodInfo[]> _handlerCache = new();
    
    // ============ 构造函数 ============
    
    protected GAgentBase(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
        Id = Guid.NewGuid();
    }
    
    protected GAgentBase(Guid id, ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
        Id = id;
    }
    
    // ============ IGAgent 实现 ============
    
    public Guid Id { get; }
    
    public virtual TState GetState() => _state;
    
    public abstract Task<string> GetDescriptionAsync();
    
    // ============ 事件发布 ============
    
    /// <summary>
    /// 发布事件（委托给 EventPublisher）
    /// </summary>
    protected async Task<string> PublishAsync<TEvent>(
        TEvent evt,
        EventDirection direction = EventDirection.Down,
        CancellationToken ct = default)
        where TEvent : IMessage
    {
        if (_eventPublisher == null)
        {
            throw new InvalidOperationException(
                "EventPublisher is not set. Make sure the Actor layer has initialized this agent.");
        }
        
        return await _eventPublisher.PublishAsync(evt, direction, ct);
    }
    
    /// <summary>
    /// 设置事件发布器（由 Actor 层调用）
    /// </summary>
    public void SetEventPublisher(IEventPublisher publisher)
    {
        _eventPublisher = publisher;
    }
    
    // ============ 事件处理器发现 ============
    
    /// <summary>
    /// 获取所有事件处理器方法（缓存）
    /// </summary>
    public MethodInfo[] GetEventHandlers()
    {
        var type = GetType();
        return _handlerCache.GetOrAdd(type, DiscoverEventHandlers);
    }
    
    /// <summary>
    /// 发现事件处理器（通过反射）
    /// </summary>
    private MethodInfo[] DiscoverEventHandlers(Type type)
    {
        var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        
        var handlers = methods
            .Where(IsEventHandlerMethod)
            .OrderBy(m => m.GetCustomAttribute<EventHandlerAttribute>()?.Priority ?? 
                         m.GetCustomAttribute<AllEventHandlerAttribute>()?.Priority ?? 
                         int.MaxValue)
            .ToArray();
        
        _logger.LogDebug("Discovered {Count} event handlers for {Type}", handlers.Length, type.Name);
        
        return handlers;
    }
    
    /// <summary>
    /// 判断是否是事件处理器方法（可被子类重写）
    /// </summary>
    protected virtual bool IsEventHandlerMethod(MethodInfo method)
    {
        var parameters = method.GetParameters();
        if (parameters.Length != 1) return false;
        
        var paramType = parameters[0].ParameterType;
        
        // [EventHandler] 标记的方法，参数必须是 IMessage
        if (method.GetCustomAttribute<EventHandlerAttribute>() != null)
        {
            return typeof(IMessage).IsAssignableFrom(paramType);
        }
        
        // [AllEventHandler] 标记的方法，参数必须是 EventEnvelope
        if (method.GetCustomAttribute<AllEventHandlerAttribute>() != null)
        {
            return paramType == typeof(EventEnvelope);
        }
        
        // 默认处理器：方法名为 HandleAsync 或 Handle，参数是 IMessage
        if (method.Name is "HandleAsync" or "Handle")
        {
            return typeof(IMessage).IsAssignableFrom(paramType) && !paramType.IsAbstract;
        }
        
        return false;
    }
    
    // ============ 事件处理器调用 ============
    
    /// <summary>
    /// 处理事件（由 Actor 层调用，可被子类重写）
    /// </summary>
    public virtual async Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        var handlers = GetEventHandlers();
        
        foreach (var handler in handlers)
        {
            try
            {
                // 检查是否允许处理自己发出的事件
                if (!ShouldHandleEvent(handler, envelope))
                    continue;
                
                var paramType = handler.GetParameters()[0].ParameterType;
                
                // AllEventHandler - 直接传递 EventEnvelope
                if (handler.GetCustomAttribute<AllEventHandlerAttribute>() != null)
                {
                    await InvokeHandler(handler, envelope, ct);
                    continue;
                }
                
                // EventHandler - 解包 Payload
                if (envelope.Payload != null)
                {
                    try
                    {
                        // 使用反射调用泛型的 Unpack<T> 方法
                        var unpackMethod = typeof(Google.Protobuf.WellKnownTypes.Any)
                            .GetMethod("Unpack", Type.EmptyTypes)
                            ?.MakeGenericMethod(paramType);
                        
                        if (unpackMethod != null)
                        {
                            var message = unpackMethod.Invoke(envelope.Payload, null);
                            
                            // 检查类型是否匹配
                            if (message != null && paramType.IsInstanceOfType(message))
                            {
                                await InvokeHandler(handler, message, ct);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Unpack 失败，可能是类型不匹配，跳过
                        _logger.LogTrace(ex, "Failed to unpack event payload for handler {Handler}", handler.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling event in {Handler}", handler.Name);
                
                // 可以选择抛出异常或继续处理其他 handler
                // 这里选择继续处理
            }
        }
    }
    
    /// <summary>
    /// 判断是否应该处理事件（可被子类使用）
    /// </summary>
    protected bool ShouldHandleEvent(MethodInfo handler, EventEnvelope envelope)
    {
        var eventHandlerAttr = handler.GetCustomAttribute<EventHandlerAttribute>();
        var allEventHandlerAttr = handler.GetCustomAttribute<AllEventHandlerAttribute>();
        
        var allowSelfHandling = eventHandlerAttr?.AllowSelfHandling ?? 
                               allEventHandlerAttr?.AllowSelfHandling ?? 
                               false;
        
        // 如果不允许处理自己的事件，且发布者是自己，则跳过
        if (!allowSelfHandling && envelope.PublisherId == Id.ToString())
        {
            return false;
        }
        
        return true;
    }
    
    /// <summary>
    /// 调用处理器方法（可被子类使用）
    /// </summary>
    protected async Task InvokeHandler(MethodInfo handler, object parameter, CancellationToken ct)
    {
        var result = handler.Invoke(this, new[] { parameter });
        
        if (result is Task task)
        {
            await task;
        }
    }
    
    // ============ 生命周期回调（可选重写） ============
    
    /// <summary>
    /// 激活回调
    /// </summary>
    public virtual Task OnActivateAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Agent {Id} activated", Id);
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// 停用回调
    /// </summary>
    public virtual Task OnDeactivateAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Agent {Id} deactivated", Id);
        return Task.CompletedTask;
    }
}
