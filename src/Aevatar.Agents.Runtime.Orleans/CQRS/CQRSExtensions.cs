using Aevatar.Agents.Abstractions.CQRS;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.Runtime.Orleans.CQRS;

/// <summary>
/// Extension methods for configuring CQRS in Orleans
/// </summary>
public static class CQRSExtensions
{
    /// <summary>
    /// Add Orleans CQRS support with state projection
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configure">Optional configuration action</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddOrleansCQRS(
        this IServiceCollection services,
        Action<StateDispatcherOptions>? configure = null)
    {
        // Configure options
        var options = new StateDispatcherOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.Configure<StateDispatcherOptions>(opt =>
        {
            opt.StreamProviderName = options.StreamProviderName;
            opt.StreamNamespace = options.StreamNamespace;
        });

        // Register StateDispatcher
        services.AddSingleton<IStateDispatcher, OrleansStateDispatcher>();

        return services;
    }

    /// <summary>
    /// Add a state projector implementation
    /// </summary>
    /// <typeparam name="TProjector">Projector type</typeparam>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddStateProjector<TProjector>(this IServiceCollection services)
        where TProjector : class, IStateProjector
    {
        services.AddSingleton<IStateProjector, TProjector>();
        return services;
    }

    /// <summary>
    /// Add a state projector instance
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="projector">Projector instance</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddStateProjector(
        this IServiceCollection services,
        IStateProjector projector)
    {
        services.AddSingleton(projector);
        return services;
    }
}

