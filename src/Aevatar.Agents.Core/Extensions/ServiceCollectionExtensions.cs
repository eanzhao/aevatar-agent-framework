using System.Linq;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventRouting;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Abstractions.Persistence;
using Aevatar.Agents.Core.DependencyInjection;
using Aevatar.Agents.Core.EventRouting;
using Aevatar.Agents.Core.EventSourcing;
using Aevatar.Agents.Core.Factory;
using Aevatar.Agents.Core.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aevatar.Agents.Core.Extensions;

/// <summary>
/// ServiceCollection extensions for GAgent configuration
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the core services required by the Aevatar Agent framework and exposes a fluent builder.
    /// </summary>
    public static IAevatarBuilder AddAevatarAgentSystem(
        this IServiceCollection services,
        Action<IAevatarBuilder>? configure = null)
    {
        return AddAevatarAgentSystem(services, configureStores: null, configure);
    }

    /// <summary>
    /// Registers the core services required by the Aevatar Agent framework and exposes a fluent builder.
    /// Allows overriding store implementations through <see cref="GAgentOptions"/>.
    /// </summary>
    public static IAevatarBuilder AddAevatarAgentSystem(
        this IServiceCollection services,
        Action<GAgentOptions>? configureStores,
        Action<IAevatarBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new GAgentOptions();
        configureStores?.Invoke(options);

        services.TryAddSingleton<IGAgentManager, GAgentManager>();
        services.TryAddSingleton<IGAgentActorFactoryProvider, DefaultGAgentActorFactoryProvider>();

        RegisterStateStore(services, options);
        RegisterConfigStore(services, options);
        RegisterEventStore(services, options);
        RegisterEventRouterStore(services, options);

        var builder = new AevatarBuilder(services);
        configure?.Invoke(builder);

        return builder;
    }

    private static void RegisterStateStore(IServiceCollection services, GAgentOptions options)
    {
        if (options.StateStoreType != null)
        {
            EnsureOpenGeneric(nameof(options.StateStoreType), typeof(IStateStore<>), options.StateStoreType);
            services.Replace(ServiceDescriptor.Singleton(typeof(IStateStore<>), options.StateStoreType));
        }
        else
        {
            services.TryAddSingleton(typeof(IStateStore<>), typeof(InMemoryStateStore<>));
        }
    }

    private static void RegisterConfigStore(IServiceCollection services, GAgentOptions options)
    {
        if (options.ConfigStoreType != null)
        {
            EnsureOpenGeneric(nameof(options.ConfigStoreType), typeof(IConfigStore<>), options.ConfigStoreType);
            services.Replace(ServiceDescriptor.Singleton(typeof(IConfigStore<>), options.ConfigStoreType));
        }
        else
        {
            services.TryAddSingleton(typeof(IConfigStore<>), typeof(InMemoryConfigStore<>));
        }
    }

    private static void RegisterEventStore(IServiceCollection services, GAgentOptions options)
    {
        if (options.EventStoreType != null)
        {
            EnsureConcrete(nameof(options.EventStoreType), typeof(IEventStore), options.EventStoreType);
            services.Replace(ServiceDescriptor.Singleton(typeof(IEventStore), options.EventStoreType));
        }
        else
        {
            services.TryAddSingleton<IEventStore, InMemoryEventStore>();
        }
    }

    private static void RegisterEventRouterStore(IServiceCollection services, GAgentOptions options)
    {
        if (options.EventRouterStoreType != null)
        {
            EnsureConcrete(nameof(options.EventRouterStoreType), typeof(IEventRouterStore),
                options.EventRouterStoreType);
            services.Replace(ServiceDescriptor.Singleton(typeof(IEventRouterStore), options.EventRouterStoreType));
        }
        else
        {
            services.TryAddSingleton<IEventRouterStore, InMemoryEventRouterStore>();
        }
    }

    private static void EnsureOpenGeneric(string optionName, Type expectedInterface, Type candidate)
    {
        if (!candidate.IsGenericTypeDefinition)
        {
            throw new ArgumentException($"{optionName} must be an open generic type.");
        }

        var genericInterfaces = candidate.GetInterfaces()
            .Concat(candidate.IsInterface ? new[] { candidate } : Array.Empty<Type>())
            .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == expectedInterface);

        if (!genericInterfaces.Any())
        {
            throw new ArgumentException($"{optionName} must implement {expectedInterface.FullName}.");
        }
    }

    private static void EnsureConcrete(string optionName, Type expectedInterface, Type candidate)
    {
        if (candidate.IsAbstract || candidate.IsInterface)
        {
            throw new ArgumentException($"{optionName} must be a concrete type.");
        }

        if (!expectedInterface.IsAssignableFrom(candidate))
        {
            throw new ArgumentException($"{optionName} must implement {expectedInterface.FullName}.");
        }
    }
}