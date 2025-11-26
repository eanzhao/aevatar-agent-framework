using Aevatar.Agents.Abstractions;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Plugins.MassTransit.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMassTransitStreamPlugin(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 1. Configure Options
        var section = configuration.GetSection("MassTransit:Stream");
        services.Configure<MassTransitStreamOptions>(section);

        var options = section.Get<MassTransitStreamOptions>() ?? new MassTransitStreamOptions();
        
        System.Console.WriteLine($"DEBUG: [AddMassTransitStreamPlugin] Loaded Options:");
        System.Console.WriteLine($"DEBUG: - TransportType: {options.TransportType}");
        System.Console.WriteLine($"DEBUG: - TopicPrefix: {options.TopicPrefix}");
        System.Console.WriteLine($"DEBUG: - RuntimeName: {options.RuntimeName}");
        System.Console.WriteLine($"DEBUG: - Kafka BootstrapServers: {options.Kafka?.BootstrapServers ?? "null"}");

        // 2. Register Provider (both concrete and interface)
        services.AddSingleton<MassTransitMessageStreamProvider>();
        services.AddSingleton<IMessageStreamProvider>(sp => sp.GetRequiredService<MassTransitMessageStreamProvider>());
        
        // 3. Register MassTransit
        services.AddMassTransit(x =>
        {
            switch (options.TransportType)
            {
                case MassTransitTransportType.InMemory:
                    x.AddConsumer<StreamMessageDispatcher>();
                    x.UsingInMemory((context, cfg) =>
                    {
                        // Configure endpoints - Consumer will be automatically subscribed to ByteArrayMessage
                        cfg.ConfigureEndpoints(context);
                    });
                    break;

                case MassTransitTransportType.Kafka:
                    // Host Bus is In-Memory
                    x.UsingInMemory((context, cfg) =>
                    {
                        cfg.ConfigureEndpoints(context);
                    });

                    x.AddRider(rider =>
                    {
                        System.Console.WriteLine("DEBUG: Adding Kafka Rider...");
                        rider.AddConsumer<StreamMessageDispatcher>();
                        
                        // Register Producer
                        rider.AddProducer<ByteArrayMessage>(options.TopicPrefix);
                        
                        rider.UsingKafka((context, k) =>
                        {
                            System.Console.WriteLine($"DEBUG: Configuring Kafka Host: {options.Kafka?.BootstrapServers ?? "null"}");
                            if (options.Kafka != null)
                            {
                                k.Host(options.Kafka.BootstrapServers);
                            }
                            
                            // Explicitly set security protocol to Plaintext to avoid SASL warnings/errors on local dev
                            k.SecurityProtocol = Confluent.Kafka.SecurityProtocol.Plaintext;

                            // Configure Topic Subscription
                            k.TopicEndpoint<ByteArrayMessage>(
                                options.TopicPrefix, 
                                options.Kafka?.ConsumerGroupId ?? "aevatar-agents-group", 
                                e =>
                                {
                                    System.Console.WriteLine($"DEBUG: Configuring Topic Endpoint: {options.TopicPrefix} for Group: {options.Kafka?.ConsumerGroupId ?? "aevatar-agents-group"}");
                                    e.AutoOffsetReset = Confluent.Kafka.AutoOffsetReset.Earliest;
                                    e.CreateIfMissing(t => 
                                    {
                                        t.NumPartitions = 1;
                                        t.ReplicationFactor = 1;
                                    });
                                    e.ConfigureConsumer<StreamMessageDispatcher>(context);
                                });
                        });
                    });
                    break;

                case MassTransitTransportType.RabbitMQ:
                    x.AddConsumer<StreamMessageDispatcher>();
                    x.UsingRabbitMq((context, cfg) =>
                    {
                        if (options.RabbitMQ != null)
                        {
                            cfg.Host(options.RabbitMQ.Host, h =>
                            {
                                h.Username(options.RabbitMQ.Username);
                                h.Password(options.RabbitMQ.Password);
                            });
                        }
                        
                        // Configure Queue Subscription
                        cfg.ReceiveEndpoint(options.TopicPrefix, e =>
                        {
                            e.ConfigureConsumer<StreamMessageDispatcher>(context);
                        });
                    });
                    break;
            }
        });

        return services;
    }
}
