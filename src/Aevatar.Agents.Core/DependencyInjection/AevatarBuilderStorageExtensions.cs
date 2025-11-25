using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Abstractions.Persistence;
using Aevatar.Agents.Core.EventSourcing;
using Aevatar.Agents.Core.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aevatar.Agents.Core.DependencyInjection;

/// <summary>
/// Storage-related helpers for <see cref="IAevatarBuilder"/>.
/// </summary>
public static class AevatarBuilderStorageExtensions
{
    /// <summary>
    /// Registers the in-memory state store as the default open generic implementation.
    /// </summary>
    public static IAevatarBuilder UseInMemoryStateStore(this IAevatarBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.Replace(ServiceDescriptor.Singleton(typeof(IStateStore<>), typeof(InMemoryStateStore<>)));
        builder.Properties[AevatarBuilderPropertyKeys.StateStore] = typeof(InMemoryStateStore<>).Name;

        return builder;
    }

    /// <summary>
    /// Registers the in-memory configuration store.
    /// </summary>
    public static IAevatarBuilder UseInMemoryConfigStore(this IAevatarBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.Replace(ServiceDescriptor.Singleton(typeof(IConfigStore<>), typeof(InMemoryConfigStore<>)));
        builder.Properties[AevatarBuilderPropertyKeys.ConfigStore] = typeof(InMemoryConfigStore<>).Name;

        return builder;
    }

    /// <summary>
    /// Registers the in-memory event store for event sourcing scenarios.
    /// </summary>
    public static IAevatarBuilder UseInMemoryEventStore(this IAevatarBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.Replace(ServiceDescriptor.Singleton(typeof(IEventStore), typeof(InMemoryEventStore)));
        builder.Properties[AevatarBuilderPropertyKeys.EventStore] = nameof(InMemoryEventStore);

        return builder;
    }

    /// <summary>
    /// Configures all stores to use in-memory implementations.
    /// </summary>
    public static IAevatarBuilder UseInMemoryStores(this IAevatarBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .UseInMemoryStateStore()
            .UseInMemoryConfigStore()
            .UseInMemoryEventStore();
    }
}

