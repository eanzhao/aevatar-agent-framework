using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Persistence;
using Aevatar.Agents.Core.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.Core.Extensions;

/// <summary>
/// ServiceCollection extensions for GAgent configuration
/// </summary>
public static class ServiceCollectionExtensions
{
    private static readonly ConcurrentDictionary<Type, object> _stateStoreCache = new();
    private static GAgentOptions? _defaultOptions;

    /// <summary>
    /// Configure default state store for all agents
    /// Must be called before ConfigGAgent
    /// </summary>
    public static IServiceCollection ConfigGAgentStateStore(
        this IServiceCollection services,
        Action<GAgentOptions> configureOptions)
    {
        if (configureOptions == null)
            throw new ArgumentNullException(nameof(configureOptions));

        var options = new GAgentOptions();
        configureOptions(options);

        // Validate
        if (options.StateStore == null && !options.EnableEventSourcing)
        {
            throw new InvalidOperationException(
                "Must specify StateStore or enable EventSourcing. " +
                "Example: options.StateStore = _ => new InMemoryStateStore()");
        }

        if (options.EnableEventSourcing && options.EventStore == null)
        {
            throw new InvalidOperationException(
                "EnableEventSourcing is true but EventStore is not configured. " +
                "Use services.AddSingleton<IEventStore>(...) to register IEventStore.");
        }

        _defaultOptions = options;
        return services;
    }

    /// <summary>
    /// Configure a specific GAgent
    /// Uses default state store if not explicitly configured
    /// </summary>
    public static IServiceCollection ConfigGAgent<TAgent, TState>(
        this IServiceCollection services,
        Action<GAgentOptions>? configureOptions = null)
        where TAgent : GAgentBase<TState>
        where TState : class, new()
    {
        // Use provided config or fall back to default
        var options = configureOptions != null ? new GAgentOptions() : _defaultOptions ?? new GAgentOptions();

        if (configureOptions != null)
        {
            configureOptions(options);
        }

        // If no StateStore configured, use InMemory as default
        if (options.StateStore == null && !options.EnableEventSourcing)
        {
            options.StateStore = _ => new InMemoryStateStore<TState>();
        }

        // Register IStateStore<TState>
        if (options.StateStore != null)
        {
            services.AddSingleton<IStateStore<TState>>(sp =>
            {
                // Use cached instance to avoid creating multiple instances
                return (IStateStore<TState>)_stateStoreCache.GetOrAdd(
                    typeof(TState),
                    _ => options.StateStore!(sp));
            });
        }

        // Register agent type for DI
        services.AddTransient<TAgent>();

        return services;
    }
}