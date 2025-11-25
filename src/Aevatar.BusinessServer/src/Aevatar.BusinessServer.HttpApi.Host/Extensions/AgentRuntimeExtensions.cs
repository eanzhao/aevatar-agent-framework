using System;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Core.EventSourcing;
using Aevatar.Agents.Core.EventDeduplication;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Runtime.Local;
using Aevatar.Agents.Runtime.Local.Subscription;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Orleans;
using Serilog;

namespace Aevatar.BusinessServer.HttpApi.Host.Extensions;

/// <summary>
/// Extension methods for configuring Agent Runtime
/// Supports Local (default) and Orleans runtimes
/// </summary>
public static class AgentRuntimeExtensions
{
    /// <summary>
    /// Add Agent Runtime to the service collection
    /// Automatically selects runtime based on configuration
    /// </summary>
    public static IServiceCollection AddAgentRuntime(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var runtimeOptions = configuration
            .GetSection(AgentRuntimeOptions.SectionName)
            .Get<AgentRuntimeOptions>() ?? new AgentRuntimeOptions();

        Log.Information("🤖 Configuring Agent Runtime:");
        Log.Information("  RuntimeType: {RuntimeType}", runtimeOptions.RuntimeType);

        // Register common services (shared by all runtimes)
        RegisterCommonServices(services);
        
        // Register factory provider (required for agent creation)
        services.AddGAgentActorFactoryProvider();
        
        // Register default IGAgentFactory (used by factory provider)
        services.TryAddSingleton<IGAgentFactory, AIGAgentFactory>();

        // Register runtime-specific services
        switch (runtimeOptions.RuntimeType)
        {
            case AgentRuntimeType.Local:
                RegisterLocalRuntime(services);
                Log.Information("  ✅ Using Local Runtime (in-memory, fast)");
                break;

            case AgentRuntimeType.Orleans:
                RegisterOrleansRuntime(services, runtimeOptions.Orleans);
                Log.Information("  ✅ Using Orleans Runtime (distributed, scalable)");
                Log.Information("     ClusterId: {ClusterId}", runtimeOptions.Orleans.ClusterId);
                Log.Information("     ServiceId: {ServiceId}", runtimeOptions.Orleans.ServiceId);
                break;

            default:
                throw new InvalidOperationException($"Unknown runtime type: {runtimeOptions.RuntimeType}");
        }

        return services;
    }

    /// <summary>
    /// Register common services shared by all runtimes
    /// </summary>
    private static void RegisterCommonServices(IServiceCollection services)
    {
        // Event Store for Event Sourcing
        services.AddSingleton<IEventStore, InMemoryEventStore>();

        // Event Deduplicator to prevent duplicate event processing
        services.AddSingleton<IEventDeduplicator>(sp =>
            new MemoryCacheEventDeduplicator(new DeduplicationOptions
            {
                EventExpiration = TimeSpan.FromMinutes(5),
                MaxCachedEvents = 10_000,
                EnableAutoCleanup = true
            }));
    }

    /// <summary>
    /// Register Local Runtime services
    /// </summary>
    private static void RegisterLocalRuntime(IServiceCollection services)
    {
        // Local runtime uses in-memory message streams
        services.AddSingleton<LocalMessageStreamRegistry>();
        
        // Actor factory for creating agents
        services.AddSingleton<IGAgentActorFactory, LocalGAgentActorFactory>();
        
        // Actor manager for lifecycle management
        services.AddSingleton<IGAgentActorManager, Aevatar.Agents.Runtime.Local.LocalGAgentActorManager>();
        
        // Subscription manager for stream subscriptions
        services.AddSingleton<ISubscriptionManager>(sp =>
            new LocalSubscriptionManager(
                sp.GetRequiredService<LocalMessageStreamRegistry>(),
                sp.GetRequiredService<ILogger<LocalSubscriptionManager>>()));
    }

    /// <summary>
    /// Register Orleans Runtime services
    /// </summary>
    private static void RegisterOrleansRuntime(IServiceCollection services, OrleansRuntimeOptions orleansOptions)
    {
        // Configure StreamingOptions for Orleans
        services.Configure<Aevatar.Agents.StreamingOptions>(options =>
        {
            options.StreamProviderName = orleansOptions.StreamProviderName;
            // Get DefaultNamespace from Streaming configuration
            var config = services.BuildServiceProvider().GetRequiredService<IConfiguration>();
            options.DefaultStreamNamespace = config.GetValue("Streaming:DefaultNamespace", "AevatarAgents");
        });

        // Orleans runtime requires Orleans Silo to be configured via UseOrleansClient
        // The actual grain factory comes from Orleans
        services.AddSingleton<IGAgentActorFactory, Aevatar.Agents.Runtime.Orleans.OrleansGAgentActorFactory>();

        // Register Orleans Actor Manager
        services.AddSingleton<IGAgentActorManager, Aevatar.Agents.Runtime.Orleans.OrleansGAgentActorManager>();

        // Ensure IGrainFactory is available (forward from IClusterClient if needed)
        services.TryAddSingleton<IGrainFactory>(sp => sp.GetRequiredService<IClusterClient>());

        // Orleans subscription manager (optional - for advanced stream management)
        // services.AddSingleton<ISubscriptionManager>(...);
    }
}
