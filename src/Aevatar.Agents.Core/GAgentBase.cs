using System.Collections.Concurrent;
using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Observability;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace Aevatar.Agents.Core;

/// <summary>
/// Agent 基类（基础版本）
/// 提供事件处理器自动发现和调用机制
/// </summary>
/// <typeparam name="TState">Agent 状态类型</typeparam>
public abstract class GAgentBase<TState> : IStateGAgent<TState>
    where TState : class, new()
{
    // ============ 字段 ============

    /// <summary>
    /// Internal state field (mutable for EventSourcing state transitions)
    /// </summary>
    private TState _state = new();
    
    /// <summary>
    /// Agent state (readonly reference, but internal field is mutable)
    /// </summary>
    protected TState State => _state;
    private ILogger _logger = NullLogger.Instance;
    protected IEventPublisher? EventPublisher;

    /// <summary>
    /// Logger 属性 - 支持自动注入
    /// </summary>
    protected ILogger Logger 
    {
        get => _logger;
        set => _logger = value ?? NullLogger.Instance;
    }

    // 事件处理器缓存（类型 -> 方法列表）
    private static readonly ConcurrentDictionary<Type, MethodInfo[]> HandlerCache = new();

    // ============ 构造函数 ============

    protected GAgentBase()
    {
        Id = Guid.NewGuid();
        // Logger 已经初始化为 NullLogger.Instance
    }

    protected GAgentBase(Guid id)
    {
        Id = id;
    }
    
    protected GAgentBase(ILogger? logger)
    {
        Logger = logger;  // 使用属性 setter，会自动处理 null
        Id = Guid.NewGuid();
    }

    protected GAgentBase(Guid id, ILogger? logger)
    {
        Id = id;
        Logger = logger;  // 使用属性 setter，会自动处理 null
    }

    // ============ IGAgent 实现 ============

    public Guid Id { get; }

    public virtual TState GetState() => State;

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
        if (EventPublisher == null)
        {
            throw new InvalidOperationException(
                "EventPublisher is not set. Make sure the Actor layer has initialized this agent.");
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var eventId = await EventPublisher.PublishEventAsync(evt, direction, ct);
            
            // 记录发布指标
            stopwatch.Stop();
            AgentMetrics.RecordEventPublished(typeof(TEvent).Name, Id.ToString());
            AgentMetrics.EventPublishLatency.Record(stopwatch.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("event.type", typeof(TEvent).Name),
                new KeyValuePair<string, object?>("agent.id", Id.ToString()));
                
            return eventId;
        }
        catch (Exception ex)
        {
            // 记录异常指标
            AgentMetrics.RecordException(ex.GetType().Name, Id.ToString(), "PublishEvent");
            throw;
        }
    }

    /// <summary>
    /// 设置事件发布器（由 Actor 层调用）
    /// </summary>
    public void SetEventPublisher(IEventPublisher publisher)
    {
        EventPublisher = publisher;
    }

    // ============ 事件处理器发现 ============

    /// <summary>
    /// 获取所有事件处理器方法（缓存）
    /// </summary>
    public MethodInfo[] GetEventHandlers()
    {
        var type = GetType();
        return HandlerCache.GetOrAdd(type, DiscoverEventHandlers);
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

        Logger.LogDebug("Discovered {Count} event handlers for {Type}", handlers.Length, type.Name);

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
        if (method.Name is "HandleAsync" or "HandleEventAsync")
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
        // 创建事件处理的日志作用域
        var eventType = envelope.Payload?.TypeUrl?.Split('/').LastOrDefault() ?? "Unknown";
        Console.WriteLine($"GAgentBase.HandleEventAsync called for agent {Id} with event type {eventType}");
        
        using var scope = LoggingScope.CreateEventHandlingScope(
            Logger, 
            Id, 
            envelope.Id, 
            eventType,
            envelope.CorrelationId);
        
        var stopwatch = Stopwatch.StartNew();
        var handled = false;
        
        var handlers = GetEventHandlers();
        Console.WriteLine($"GAgentBase.HandleEventAsync found {handlers.Count()} handlers");

        foreach (var handler in handlers)
        {
            try
            {
                var paramType = handler.GetParameters()[0].ParameterType;
                Console.WriteLine($"GAgentBase: Processing handler {handler.Name} with parameter type {paramType.Name}");
                
                // 检查是否允许处理自己发出的事件
                if (!ShouldHandleEvent(handler, envelope))
                {
                    Console.WriteLine($"GAgentBase: Handler {handler.Name} skipped by ShouldHandleEvent");
                    continue;
                }

                // AllEventHandler - 直接传递 EventEnvelope
                if (handler.GetCustomAttribute<AllEventHandlerAttribute>() != null)
                {
                    await InvokeHandler(handler, envelope, ct);
                    handled = true;
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
                            Logger.LogDebug("Unpacked message of type {MessageType} for handler {HandlerName}", 
                                message?.GetType().Name ?? "null", handler.Name);
                        
                            // 检查类型是否匹配
                            if (message != null && paramType.IsInstanceOfType(message))
                            {
                                Console.WriteLine($"GAgentBase: Invoking handler {handler.Name} with message type {message.GetType().Name}");
                                Logger.LogDebug("Invoking handler {HandlerName} with message {MessageType}", 
                                    handler.Name, message.GetType().Name);
                                await InvokeHandler(handler, message, ct);
                                handled = true;
                                Console.WriteLine($"GAgentBase: Handler {handler.Name} completed successfully");
                            }
                            else
                            {
                                Console.WriteLine($"GAgentBase: Type mismatch - handler {handler.Name} expects {paramType.Name}, got {message?.GetType().Name ?? "null"}");
                                Logger.LogDebug("Type mismatch: handler {HandlerName} expects {ExpectedType}, got {ActualType}",
                                    handler.Name, paramType.Name, message?.GetType().Name ?? "null");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Unpack 失败，可能是类型不匹配，跳过
                        Logger.LogTrace(ex, "Failed to unpack event payload for handler {Handler}", handler.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error handling event in {Handler}", handler.Name);
                
                // 记录异常指标
                AgentMetrics.RecordException(ex.GetType().Name, Id.ToString(), $"HandleEvent:{handler.Name}");

                // 发布异常事件
                await PublishExceptionEventAsync(envelope, handler.Name, ex);

                // 继续处理其他 handler
            }
        }
        
        // 记录事件处理指标
        stopwatch.Stop();
        if (handled)
        {
            AgentMetrics.RecordEventHandled(eventType, Id.ToString(), stopwatch.ElapsedMilliseconds);
        }
        else
        {
            // 没有处理器处理这个事件
            AgentMetrics.EventsDropped.Add(1, 
                new KeyValuePair<string, object?>("event.type", eventType),
                new KeyValuePair<string, object?>("agent.id", Id.ToString()));
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
        Logger.LogDebug("Invoking handler method {HandlerName} on {AgentType} with parameter type {ParameterType}",
            handler.Name, GetType().Name, parameter.GetType().Name);
        
        var result = handler.Invoke(this, new[] { parameter });

        if (result is Task task)
        {
            await task;
        }
        
        Logger.LogDebug("Handler method {HandlerName} completed", handler.Name);
    }

    // ============ 事件订阅信息 ============

    /// <summary>
    /// 获取所有订阅的事件类型
    /// </summary>
    public virtual Task<List<Type>> GetAllSubscribedEventsAsync(bool includeAllEventHandler = false)
    {
        var handlers = GetEventHandlers();
        var eventTypes = new HashSet<Type>();

        foreach (var handler in handlers)
        {
            var paramType = handler.GetParameters().FirstOrDefault()?.ParameterType;

            if (paramType == null)
                continue;

            // 跳过 EventEnvelope（AllEventHandler）
            if (!includeAllEventHandler && paramType == typeof(EventEnvelope))
                continue;

            // 只包含 IMessage 类型
            if (typeof(Google.Protobuf.IMessage).IsAssignableFrom(paramType))
            {
                eventTypes.Add(paramType);
            }
        }

        return Task.FromResult(eventTypes.ToList());
    }

    // ============ 资源管理 ============

    /// <summary>
    /// 准备资源上下文
    /// </summary>
    public virtual Task PrepareResourceContextAsync(ResourceContext context, CancellationToken ct = default)
    {
        Logger.LogDebug("Preparing resource context for Agent {Id} with {ResourceCount} resources",
            Id, context.AvailableResources.Count);

        return OnPrepareResourceContextAsync(context, ct);
    }

    /// <summary>
    /// 资源上下文准备回调（由子类重写）
    /// </summary>
    protected virtual Task OnPrepareResourceContextAsync(ResourceContext context, CancellationToken ct = default)
    {
        // 默认实现：什么都不做
        // 子类可以重写此方法来处理资源
        return Task.CompletedTask;
    }

    // ============ 异常处理 ============

    /// <summary>
    /// 发布异常事件
    /// </summary>
    protected virtual async Task PublishExceptionEventAsync(
        EventEnvelope originalEnvelope,
        string handlerName,
        Exception exception)
    {
        try
        {
            if (EventPublisher == null)
                return;

            var exceptionEvent = new EventHandlerExceptionEvent
            {
                AgentId = Id.ToString(),
                EventId = originalEnvelope.Id,
                HandlerName = handlerName,
                EventType = originalEnvelope.Payload?.TypeUrl ?? "Unknown",
                ExceptionMessage = exception.Message,
                StackTrace = exception.StackTrace ?? string.Empty,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            Logger.LogDebug("Publishing exception event for handler {Handler}", handlerName);

            await EventPublisher.PublishEventAsync(exceptionEvent, EventDirection.Up);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error publishing exception event");
        }
    }

    /// <summary>
    /// 发布框架异常事件
    /// </summary>
    protected virtual async Task PublishFrameworkExceptionAsync(
        string operation,
        Exception exception)
    {
        try
        {
            if (EventPublisher == null)
                return;

            var exceptionEvent = new GAgentBaseExceptionEvent
            {
                AgentId = Id.ToString(),
                Operation = operation,
                ExceptionMessage = exception.Message,
                StackTrace = exception.StackTrace ?? string.Empty,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            Logger.LogDebug("Publishing framework exception event for operation {Operation}", operation);

            await EventPublisher.PublishEventAsync(exceptionEvent, EventDirection.Up);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error publishing framework exception event");
        }
    }

    // ============ 生命周期回调（可选重写） ============

    /// <summary>
    /// 激活回调
    /// </summary>
    public virtual Task OnActivateAsync(CancellationToken ct = default)
    {
        Logger.LogDebug("Agent {Id} activated", Id);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 停用回调
    /// </summary>
    public virtual Task OnDeactivateAsync(CancellationToken ct = default)
    {
        Logger.LogDebug("Agent {Id} deactivated", Id);
        return Task.CompletedTask;
    }
}