using Aevatar.Agents.Abstractions.EventSourcing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Orleans.EventSourcing;

/// <summary>
/// Extension methods for registering EventSourcing services in test environments
/// </summary>
public static class EventSourcingTestExtensions
{
    /// <summary>
    /// Register in-memory EventSourcing services for testing
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection for chaining</returns>
    /// <remarks>
    /// This registers:
    /// - InMemoryEventRepository as IEventRepository (singleton)
    /// - OrleansEventStore as IEventStore (singleton)
    /// 
    /// Use this in test environments instead of production MongoDB configuration.
    /// </remarks>
    public static IServiceCollection AddInMemoryEventSourcing(this IServiceCollection services)
    {
        // Register in-memory repository
        services.AddSingleton<IEventRepository, InMemoryEventRepository>();
        
        // Register Orleans event store with in-memory repository
        services.AddSingleton<IEventStore, OrleansEventStore>();
        
        return services;
    }
    
    /// <summary>
    /// Get the in-memory event repository instance for test assertions
    /// </summary>
    /// <param name="services">Service provider</param>
    /// <returns>InMemoryEventRepository instance or null if not registered</returns>
    public static InMemoryEventRepository? GetInMemoryEventRepository(this IServiceProvider services)
    {
        return services.GetService<IEventRepository>() as InMemoryEventRepository;
    }
}

