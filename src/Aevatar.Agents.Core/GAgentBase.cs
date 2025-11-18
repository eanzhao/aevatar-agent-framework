using System.Collections.Concurrent;
using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core.Observability;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using Aevatar.Agents.Abstractions.Helpers;
using Google.Protobuf.WellKnownTypes;
using Type = System.Type;

namespace Aevatar.Agents.Core;

/// <summary>
/// Non-generic base class for all GAgents.
/// Provides event handler auto-discovery and invocation infrastructure.
/// This class focuses solely on event processing without state management concerns.
/// </summary>
public abstract class GAgentBase(Guid id) : IGAgent
{
    // ============ Fields ============

    /// <summary>
    /// Agent unique identifier
    /// </summary>
    public Guid Id { get; } = id;

    /// <summary>
    /// Event publisher for sending events
    /// </summary>
    protected IEventPublisher? EventPublisher;

    /// <summary>
    /// Logger property - supports automatic injection
    /// </summary>
    protected ILogger Logger => NullLogger.Instance;

    // Event handler cache (type -> method list)
    private static readonly ConcurrentDictionary<Type, MethodInfo[]> HandlerCache = new();

    // ============ Constructors ============

    public GAgentBase() : this(Guid.NewGuid())
    {
    }

    // ============ IGAgent Implementation ============



    /// <summary>
    /// Get agent description
    /// </summary>
    public virtual string GetDescription()
    {
        return GetType().Name;
    }

    /// <summary>
    /// Get agent description - async version
    /// </summary>
    /// <returns></returns>
    public virtual Task<string> GetDescriptionAsync()
    {
        return Task.FromResult(GetDescription());
    }

    /// <summary>
    /// Get all subscribed event types
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

            // Skip EventEnvelope (AllEventHandler) if not requested
            if (!includeAllEventHandler && paramType == typeof(EventEnvelope))
                continue;

            // Only include IMessage types
            if (typeof(IMessage).IsAssignableFrom(paramType))
            {
                eventTypes.Add(paramType);
            }
        }

        return Task.FromResult(eventTypes.ToList());
    }

    public async Task ActivateAsync()
    {
        await OnActivateAsync();
    }

    public async Task DeactivateAsync()
    {
        await OnDeactivateAsync();
    }

    // ============ Event Publishing ============

    /// <summary>
    /// Publish event (delegates to EventPublisher)
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

            // Record publish metrics
            stopwatch.Stop();
            AgentMetrics.RecordEventPublished(typeof(TEvent).Name, Id.ToString());
            AgentMetrics.EventPublishLatency.Record(stopwatch.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("event.type", typeof(TEvent).Name),
                new KeyValuePair<string, object?>("agent.id", Id.ToString()));

            return eventId;
        }
        catch (Exception ex)
        {
            // Record exception metrics
            AgentMetrics.RecordException(ex.GetType().Name, Id.ToString(), "PublishEvent");
            throw;
        }
    }

    // EventPublisher is now injected via EventPublisherInjector
    // No public setter method needed

    // ============ Event Handler Discovery ============

    /// <summary>
    /// Get all event handler methods (cached)
    /// </summary>
    public MethodInfo[] GetEventHandlers()
    {
        var type = GetType();
        return HandlerCache.GetOrAdd(type, DiscoverEventHandlers);
    }

    /// <summary>
    /// Discover event handlers via reflection
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
    /// Determine if a method is an event handler (can be overridden by subclasses)
    /// </summary>
    protected virtual bool IsEventHandlerMethod(MethodInfo method)
    {
        var parameters = method.GetParameters();
        if (parameters.Length != 1) return false;

        var paramType = parameters[0].ParameterType;

        // [EventHandler] marked methods, parameter must be IMessage
        if (method.GetCustomAttribute<EventHandlerAttribute>() != null)
        {
            return typeof(IMessage).IsAssignableFrom(paramType);
        }

        // [AllEventHandler] marked methods, parameter must be EventEnvelope
        if (method.GetCustomAttribute<AllEventHandlerAttribute>() != null)
        {
            return paramType == typeof(EventEnvelope);
        }

        // Convention-based handlers: method named HandleAsync or HandleEventAsync, parameter is IMessage
        if (method.Name is "HandleAsync" or "HandleEventAsync")
        {
            return typeof(IMessage).IsAssignableFrom(paramType) && !paramType.IsAbstract;
        }

        return false;
    }

    // ============ Event Handler Invocation ============

    /// <summary>
    /// Handle event - entry point that can be overridden by derived classes
    /// Derived classes should override this to add state management
    /// </summary>
    public virtual async Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        await HandleEventCoreAsync(envelope, ct);
    }

    /// <summary>
    /// Core event handling implementation without state management
    /// This method contains the actual event processing logic
    /// </summary>
    protected virtual async Task HandleEventCoreAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        // Create event handling log scope
        var eventType = envelope.Payload?.TypeUrl?.Split('/').LastOrDefault() ?? "Unknown";

        using var scope = LoggingScope.CreateEventHandlingScope(
            Logger,
            Id,
            envelope.Id,
            eventType,
            envelope.CorrelationId);

        var stopwatch = Stopwatch.StartNew();
        var handled = false;

        var handlers = GetEventHandlers();

        foreach (var handler in handlers)
        {
            try
            {
                var paramType = handler.GetParameters()[0].ParameterType;

                // Check if should handle event
                if (!ShouldHandleEvent(handler, envelope))
                {
                    continue;
                }

                // AllEventHandler - pass EventEnvelope directly
                if (handler.GetCustomAttribute<AllEventHandlerAttribute>() != null)
                {
                    await InvokeHandler(handler, envelope, ct);
                    handled = true;
                    continue;
                }

                // EventHandler - unpack Payload
                if (envelope.Payload != null)
                {
                    object? message = null;
                    try
                    {
                        // Use reflection to call generic Unpack<T> method
                        var unpackMethod = typeof(Any)
                            .GetMethod("Unpack", Type.EmptyTypes)
                            ?.MakeGenericMethod(paramType);

                        if (unpackMethod != null)
                        {
                            message = unpackMethod.Invoke(envelope.Payload, null);
                            Logger.LogDebug("Unpacked message of type {MessageType} for handler {HandlerName}",
                                message?.GetType().Name ?? "null", handler.Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Unpack failed, possibly type mismatch, skip
                        Logger.LogTrace(ex, "Failed to unpack event payload for handler {Handler}", handler.Name);
                        continue;
                    }

                    // Check if type matches and invoke handler
                    if (message != null && paramType.IsInstanceOfType(message))
                    {
                        Logger.LogDebug("Invoking handler {HandlerName} with message {MessageType}",
                            handler.Name, message.GetType().Name);
                        await InvokeHandler(handler, message, ct);
                        handled = true;
                    }
                    else
                    {
                        Logger.LogDebug(
                            "Type mismatch: handler {HandlerName} expects {ExpectedType}, got {ActualType}",
                            handler.Name, paramType.Name, message?.GetType().Name ?? "null");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error handling event in {Handler}", handler.Name);

                // Record exception metrics
                AgentMetrics.RecordException(ex.GetType().Name, Id.ToString(), $"HandleEvent:{handler.Name}");

                // Publish exception event
                await PublishExceptionEventAsync(envelope, handler.Name, ex);

                // Continue processing other handlers
            }
        }

        // Record event handling metrics
        stopwatch.Stop();
        if (handled)
        {
            AgentMetrics.RecordEventHandled(eventType, Id.ToString(), stopwatch.ElapsedMilliseconds);
        }
        else
        {
            // No handler processed this event
            AgentMetrics.EventsDropped.Add(1,
                new KeyValuePair<string, object?>("event.type", eventType),
                new KeyValuePair<string, object?>("agent.id", Id.ToString()));
        }
    }

    /// <summary>
    /// Determine if an event should be handled (can be used by subclasses)
    /// </summary>
    protected bool ShouldHandleEvent(MethodInfo handler, EventEnvelope envelope)
    {
        var eventHandlerAttr = handler.GetCustomAttribute<EventHandlerAttribute>();
        var allEventHandlerAttr = handler.GetCustomAttribute<AllEventHandlerAttribute>();

        var allowSelfHandling = eventHandlerAttr?.AllowSelfHandling ??
                                allEventHandlerAttr?.AllowSelfHandling ??
                                false;

        // If self-handling is not allowed and publisher is self, skip
        if (!allowSelfHandling && envelope.PublisherId == Id.ToString())
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Invoke handler method (can be used by subclasses)
    /// </summary>
    protected async Task InvokeHandler(MethodInfo handler, object parameter, CancellationToken ct)
    {
        Logger.LogDebug("Invoking handler method {HandlerName} on {AgentType} with parameter type {ParameterType}",
            handler.Name, GetType().Name, parameter.GetType().Name);

        try
        {
            var result = handler.Invoke(this, new[] { parameter });

            if (result is Task task)
            {
                await task;
            }

            Logger.LogDebug("Handler method {HandlerName} completed", handler.Name);
        }
        catch (TargetInvocationException tie) when (tie.InnerException != null)
        {
            // Unwrap the TargetInvocationException to get the actual exception
            throw tie.InnerException;
        }
    }

    // ============ Resource Management ============

    /// <summary>
    /// Prepare resource context
    /// </summary>
    public virtual Task PrepareResourceContextAsync(ResourceContext context, CancellationToken ct = default)
    {
        Logger.LogDebug("Preparing resource context for Agent {Id} with {ResourceCount} resources",
            Id, context.AvailableResources.Count);

        return OnPrepareResourceContextAsync(context, ct);
    }

    /// <summary>
    /// Resource context preparation callback (overridden by subclasses)
    /// </summary>
    protected virtual Task OnPrepareResourceContextAsync(ResourceContext context, CancellationToken ct = default)
    {
        // Default implementation: do nothing
        // Subclasses can override to handle resources
        return Task.CompletedTask;
    }

    // ============ Exception Handling ============

    /// <summary>
    /// Publish exception event
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

            // Build complete exception message including inner exceptions
            var fullExceptionMessage = BuildFullExceptionMessage(exception);
            
            var exceptionEvent = new EventHandlerExceptionEvent
            {
                AgentId = Id.ToString(),
                EventId = originalEnvelope.Id,
                HandlerName = handlerName,
                EventType = originalEnvelope.Payload?.TypeUrl ?? "Unknown",
                ExceptionMessage = fullExceptionMessage,
                StackTrace = exception.StackTrace ?? string.Empty,
                Timestamp = TimestampHelper.GetUtcNow()
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
    /// Build a complete exception message including all inner exceptions
    /// </summary>
    private static string BuildFullExceptionMessage(Exception exception)
    {
        var messages = new List<string>();
        var currentException = exception;
        var level = 0;
        
        while (currentException != null && level < 10) // Limit depth to prevent infinite loops
        {
            if (level == 0)
            {
                messages.Add($"{currentException.GetType().Name}: {currentException.Message}");
            }
            else
            {
                messages.Add($" ---> {currentException.GetType().Name}: {currentException.Message}");
            }
            
            currentException = currentException.InnerException;
            level++;
        }
        
        if (level >= 10 && currentException != null)
        {
            messages.Add(" ---> [Exception chain truncated]");
        }
        
        return string.Join(Environment.NewLine, messages);
    }

    /// <summary>
    /// Publish framework exception event
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
                Timestamp = TimestampHelper.GetUtcNow()
            };

            Logger.LogDebug("Publishing framework exception event for operation {Operation}", operation);

            await EventPublisher.PublishEventAsync(exceptionEvent, EventDirection.Up);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error publishing framework exception event");
        }
    }

    // ============ Lifecycle Callbacks (optional override) ============

    /// <summary>
    /// Activation callback
    /// </summary>
    protected virtual Task OnActivateAsync(CancellationToken ct = default)
    {
        Logger.LogDebug("Agent {Id} activated", Id);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Deactivation callback
    /// </summary>
    protected virtual Task OnDeactivateAsync(CancellationToken ct = default)
    {
        Logger.LogDebug("Agent {Id} deactivated", Id);
        return Task.CompletedTask;
    }
}