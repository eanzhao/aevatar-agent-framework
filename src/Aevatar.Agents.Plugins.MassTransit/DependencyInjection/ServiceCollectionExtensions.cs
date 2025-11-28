using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes; // Added
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection; // Added
using Microsoft.Extensions.Logging; 
using Confluent.Kafka; 

namespace Aevatar.Agents.Plugins.MassTransit.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds MassTransit Message Stream plugin with automatic agent assembly discovery.
    /// Scans all loaded assemblies for IGAgent implementations.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">The configuration</param>
    /// <returns>The service collection</returns>
    public static IServiceCollection AddMassTransitStreamPlugin(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var agentAssemblies = DiscoverAgentAssemblies();
        return services.AddMassTransitStreamPlugin(configuration, agentAssemblies);
    }
    
    /// <summary>
    /// Adds MassTransit Message Stream plugin with assemblies discovered by naming pattern.
    /// Useful when agent assemblies follow a naming convention (e.g., "*.Agents.*", "MyCompany.Agents.*")
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">The configuration</param>
    /// <param name="assemblyNamePatterns">Assembly name patterns to match (supports * wildcard)</param>
    /// <returns>The service collection</returns>
    public static IServiceCollection AddMassTransitStreamPluginWithPatterns(
        this IServiceCollection services,
        IConfiguration configuration,
        params string[] assemblyNamePatterns)
    {
        var agentAssemblies = DiscoverAgentAssembliesByPattern(assemblyNamePatterns);
        return services.AddMassTransitStreamPlugin(configuration, agentAssemblies);
    }
    
    /// <summary>
    /// Discovers all loaded assemblies that contain IGAgent implementations.
    /// </summary>
    /// <returns>Array of assemblies containing agents</returns>
    public static Assembly[] DiscoverAgentAssemblies()
    {
        var agentAssemblies = new List<Assembly>();
        
        try
        {
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location));
                
            foreach (var assembly in loadedAssemblies)
            {
                try
                {
                    var hasAgents = assembly.GetTypes()
                        .Any(t => typeof(IGAgent).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);
                    
                    if (hasAgents)
                    {
                        agentAssemblies.Add(assembly);
                        Console.WriteLine($"DEBUG: Auto-discovered agent assembly: {assembly.GetName().Name}");
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                    // Skip assemblies that fail to load types
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARNING: Failed to auto-discover agent assemblies: {ex.Message}");
        }
        
        Console.WriteLine($"DEBUG: Auto-discovered {agentAssemblies.Count} agent assemblies");
        return agentAssemblies.ToArray();
    }
    
    /// <summary>
    /// Discovers assemblies by name patterns (supports * wildcard).
    /// </summary>
    /// <param name="patterns">Assembly name patterns (e.g., "*.Agents.*", "MyCompany.Agents.*")</param>
    /// <returns>Array of matching assemblies</returns>
    public static Assembly[] DiscoverAgentAssembliesByPattern(params string[] patterns)
    {
        if (patterns == null || patterns.Length == 0)
            return DiscoverAgentAssemblies();
            
        var agentAssemblies = new List<Assembly>();
        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location));
            
        foreach (var assembly in loadedAssemblies)
        {
            var assemblyName = assembly.GetName().Name ?? string.Empty;
            
            foreach (var pattern in patterns)
            {
                if (MatchesPattern(assemblyName, pattern))
                {
                    try
                    {
                        var hasAgents = assembly.GetTypes()
                            .Any(t => typeof(IGAgent).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);
                        
                        if (hasAgents)
                        {
                            agentAssemblies.Add(assembly);
                            Console.WriteLine($"DEBUG: Pattern-matched agent assembly: {assemblyName} (pattern: {pattern})");
                            break;
                        }
                    }
                    catch (ReflectionTypeLoadException)
                    {
                        // Skip assemblies that fail to load types
                    }
                }
            }
        }
        
        Console.WriteLine($"DEBUG: Pattern-discovered {agentAssemblies.Count} agent assemblies");
        return agentAssemblies.ToArray();
    }
    
    private static bool MatchesPattern(string input, string pattern)
    {
        // Simple wildcard matching: * matches any sequence
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(input, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
    
    /// <summary>
    /// Adds MassTransit Message Stream plugin with explicit assembly list.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">The configuration</param>
    /// <param name="agentAssemblies">Assemblies containing Agent definitions to scan for [StreamTopic] attributes</param>
    /// <returns>The service collection</returns>
    public static IServiceCollection AddMassTransitStreamPlugin(
        this IServiceCollection services,
        IConfiguration configuration,
        params Assembly[] agentAssemblies)
    {
        // 1. Configure Options
        var section = configuration.GetSection("MassTransit:Stream");
        services.Configure<MassTransitStreamOptions>(section);

        // Create options instance to merge config and annotations
        var options = section.Get<MassTransitStreamOptions>() ?? new MassTransitStreamOptions();
        
        // Collect all topics to subscribe to and produce to
        var allTopics = new HashSet<string>();
        
        // Always add the default TopicPrefix
        if (!string.IsNullOrEmpty(options.TopicPrefix))
        {
            allTopics.Add(options.TopicPrefix);
        }
        
        // Add additional topics from configuration
        if (options.Topics != null)
        {
            foreach (var t in options.Topics)
            {
                if (!string.IsNullOrEmpty(t)) allTopics.Add(t);
            }
        }

        // Add topics from configured mapping
        if (options.TopicMapping != null)
        {
            foreach (var t in options.TopicMapping.Values)
            {
                if (!string.IsNullOrEmpty(t)) allTopics.Add(t);
            }
        }

        // 1.1 Scan Assemblies for [StreamTopic]
        if (agentAssemblies != null && agentAssemblies.Length > 0)
        {
            System.Console.WriteLine($"DEBUG: Scanning {agentAssemblies.Length} assemblies for [StreamTopic]...");
            foreach (var assembly in agentAssemblies)
            {
                try 
                {
                    var agentTypes = assembly.GetTypes()
                        .Where(t => typeof(IGAgent).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

                    foreach (var type in agentTypes)
                    {
                        var attr = type.GetCustomAttribute<StreamTopicAttribute>();
                        if (attr != null)
                        {
                            var category = type.Name;
                            var topic = attr.Topic;
                            
                            System.Console.WriteLine($"DEBUG: Found [StreamTopic] for Agent {category} -> {topic}");

                            // Add to Mapping (for Producer routing)
                            // Note: This modifies the local 'options' object used for setup, 
                            // but NOT the IOptions registered in DI. 
                            // We need to ensure MassTransitMessageStream uses the merged mapping.
                            if (!options.TopicMapping.ContainsKey(category))
                            {
                                options.TopicMapping[category] = topic;
                            }
                            
                            // Add to Subscription (for Consumer)
                            allTopics.Add(topic);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"WARNING: Failed to scan assembly {assembly.FullName} for agents: {ex.Message}");
                }
            }
        }
        
        // CRITICAL: Update the registered options with the merged mapping
        // Since we modified 'options' locally, we need to replace the IOptions registration or configure it.
        services.PostConfigure<MassTransitStreamOptions>(o => 
        {
            foreach (var kvp in options.TopicMapping)
            {
                if (!o.TopicMapping.ContainsKey(kvp.Key))
                {
                    o.TopicMapping[kvp.Key] = kvp.Value;
                }
            }
        });

        System.Console.WriteLine($"DEBUG: [AddMassTransitStreamPlugin] Final Configuration:");
        System.Console.WriteLine($"DEBUG: - TransportType: {options.TransportType}");
        System.Console.WriteLine($"DEBUG: - Total Topics: {allTopics.Count}");
        System.Console.WriteLine($"DEBUG: - Mapped Categories: {options.TopicMapping.Count}");

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
                        
                        // Register Producers for ALL topics
                        // This allows MassTransitMessageStream to dynamically produce to any configured topic
                        foreach (var topic in allTopics)
                        {
                            rider.AddProducer<Guid, ByteArrayMessage>(topic);
                        }
                        
                        rider.UsingKafka((context, k) =>
                        {
                            System.Console.WriteLine($"DEBUG: Configuring Kafka Host: {options.Kafka?.BootstrapServers ?? "null"}");
                            if (options.Kafka != null)
                            {
                                k.Host(options.Kafka.BootstrapServers);
                            }
                            
                            // Explicitly set security protocol to Plaintext to avoid SASL warnings/errors on local dev
                            k.SecurityProtocol = Confluent.Kafka.SecurityProtocol.Plaintext;

                            System.Console.WriteLine($"DEBUG: Subscribing to {allTopics.Count} topics: {string.Join(", ", allTopics)}");

                            foreach (var topic in allTopics)
                            {
                                // Configure Topic Subscription for each topic
                                k.TopicEndpoint<ByteArrayMessage>(
                                    topic, 
                                    options.Kafka?.ConsumerGroupId ?? "aevatar-agents-group", 
                                    e =>
                                    {
                                        System.Console.WriteLine($"DEBUG: Configuring Topic Endpoint: {topic} for Group: {options.Kafka?.ConsumerGroupId ?? "aevatar-agents-group"}");
                                        e.AutoOffsetReset = Confluent.Kafka.AutoOffsetReset.Earliest;
                                        
                                        // Optimize Concurrency & Prefetch (Phase 2 Optimization)
                                        e.UseConcurrencyLimit(50); // Increase concurrency
                                        e.PrefetchCount = 200;     // Increase prefetch
                                        
                                        // Note: We rely on Kafka Partition Ordering (Producer uses StreamId as Key)
                                        // so we don't need explicit UsePartitioner here for ordering.
                                        // Consumer processes partitions sequentially by default.

                                        // Optimize Checkpoint (Batch Commit)
                                        e.CheckpointInterval = TimeSpan.FromSeconds(5);
                                        e.CheckpointMessageCount = 100;
                                        
                                        e.CreateIfMissing(t =>
                                        {
                                            t.NumPartitions = 8; // Match Orleans default
                                            t.ReplicationFactor = 1;
                                        });
                                        e.ConfigureConsumer<StreamMessageDispatcher>(context);
                                    });
                            }
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
