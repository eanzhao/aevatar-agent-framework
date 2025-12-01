using Aevatar.Agents.Abstractions.CQRS;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Aevatar.Agents.Plugins.MassTransit.CQRS;

/// <summary>
/// Extension methods for configuring MassTransit-based CQRS
/// </summary>
public static class CQRSMassTransitExtensions
{
    /// <summary>
    /// Add MassTransit-based CQRS state projection services.
    /// Use this instead of Orleans CQRS when using MassTransit as your message broker.
    /// </summary>
    public static IServiceCollection AddMassTransitCQRS(
        this IServiceCollection services,
        Action<MassTransitStateDispatcherOptions>? configureOptions = null)
    {
        // Configure options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // Register dispatcher
        services.AddSingleton<IStateDispatcher, MassTransitStateDispatcher>();

        return services;
    }

    /// <summary>
    /// Configure MassTransit to receive state projection messages.
    /// Call this when configuring MassTransit bus.
    /// </summary>
    /// <example>
    /// services.AddMassTransit(x =>
    /// {
    ///     x.AddStateProjectionConsumer();
    ///     x.UsingRabbitMq((context, cfg) =>
    ///     {
    ///         cfg.ConfigureStateProjectionEndpoint(context);
    ///     });
    /// });
    /// </example>
    public static void AddStateProjectionConsumer(this IBusRegistrationConfigurator configurator)
    {
        configurator.AddConsumer<StateProjectionConsumer>();
    }

    /// <summary>
    /// Configure the state projection receive endpoint.
    /// </summary>
    public static void ConfigureStateProjectionEndpoint(
        this IRabbitMqBusFactoryConfigurator cfg,
        IBusRegistrationContext context,
        string queueName = "state-projection",
        int prefetchCount = 16)
    {
        cfg.ReceiveEndpoint(queueName, e =>
        {
            e.PrefetchCount = prefetchCount;
            e.ConfigureConsumer<StateProjectionConsumer>(context);
        });
    }
}

