using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Abstractions.Persistence;
using Aevatar.Agents.Core.Observability;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core;

/// <summary>
/// Agent base class with configuration support
/// Provides separate state and configuration persistence
/// </summary>
/// <typeparam name="TState">Agent state type</typeparam>
/// <typeparam name="TConfig">Agent configuration type</typeparam>
public abstract class GAgentBase<TState, TConfig> : GAgentBase<TState>
    where TState : class, new()
    where TConfig : class, new()
{
    /// <summary>
    /// Configuration object (writable, automatically persisted to ConfigStore)
    /// </summary>
    protected TConfig Config { get; set; } = new TConfig();

    /// <summary>
    /// Configuration store (injected by Actor layer)
    /// </summary>
    public IConfigurationStore<TConfig>? ConfigStore { get; set; }

    /// <summary>
    /// Handle event with automatic configuration loading/saving
    /// </summary>
    public override async Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        // 1. Load Configuration (if ConfigStore is configured)
        if (ConfigStore != null)
        {
            Config = await ConfigStore.LoadAsync(Id, ct) ?? new TConfig();
        }

        // 2. Load State (if StateStore is configured)
        if (StateStore != null)
        {
            State = await StateStore.LoadAsync(Id, ct) ?? new TState();
        }

        // 3. Create log scope
        var eventType = envelope.Payload?.TypeUrl?.Split('/').LastOrDefault() ?? "Unknown";

        using var scope = LoggingScope.CreateEventHandlingScope(
            Logger,
            Id,
            envelope.Id,
            eventType,
            envelope.CorrelationId);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
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
                    try
                    {
                        var unpackMethod = typeof(Google.Protobuf.WellKnownTypes.Any)
                            .GetMethod("Unpack", System.Type.EmptyTypes)
                            ?.MakeGenericMethod(paramType);

                        if (unpackMethod != null)
                        {
                            var message = unpackMethod.Invoke(envelope.Payload, null);

                            if (message != null && paramType.IsInstanceOfType(message))
                            {
                                await InvokeHandler(handler, message, ct);
                                handled = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogTrace(ex, "Failed to unpack event payload for handler {Handler}", handler.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error handling event in {Handler}", handler.Name);
                AgentMetrics.RecordException(ex.GetType().Name, Id.ToString(), $"HandleEvent:{handler.Name}");
                await PublishExceptionEventAsync(envelope, handler.Name, ex);
            }
        }

        // 4. Save Configuration (if ConfigStore is configured)
        if (ConfigStore != null)
        {
            await ConfigStore.SaveAsync(Id, Config, ct);
        }

        // 5. Save State (if StateStore is configured)
        if (StateStore != null)
        {
            await StateStore.SaveAsync(Id, State, ct);
        }

        // Record metrics
        stopwatch.Stop();
        if (handled)
        {
            AgentMetrics.RecordEventHandled(eventType, Id.ToString(), stopwatch.ElapsedMilliseconds);
        }
        else
        {
            AgentMetrics.EventsDropped.Add(1,
                new KeyValuePair<string, object?>("event.type", eventType),
                new KeyValuePair<string, object?>("agent.id", Id.ToString()));
        }
    }

    /// <summary>
    /// Get configuration (for agents that need to read config)
    /// </summary>
    public TConfig GetConfig() => Config;
}
