using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Orleans runtime implementation for distributed agent execution.
/// Provides Orleans-based clustering and virtual actor support.
/// </summary>
public class OrleansAgentRuntime : IAgentRuntime
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OrleansAgentRuntime> _logger;
    private readonly ConcurrentDictionary<Guid, OrleansAgentHost> _hosts;
    private volatile bool _isShuttingDown;

    /// <inheritdoc />
    public string RuntimeType => "Orleans";

    /// <summary>
    /// Initializes a new instance of the <see cref="OrleansAgentRuntime"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider for dependency injection.</param>
    /// <param name="logger">The logger instance.</param>
    public OrleansAgentRuntime(IServiceProvider serviceProvider, ILogger<OrleansAgentRuntime>? logger = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<OrleansAgentRuntime>.Instance;
        _hosts = new ConcurrentDictionary<Guid, OrleansAgentHost>();
        
        _logger.LogInformation("OrleansAgentRuntime initialized");
    }

    /// <inheritdoc />
    public async Task<IAgentHost> CreateHostAsync(AgentHostConfiguration config)
    {
        if (_isShuttingDown)
        {
            throw new InvalidOperationException("Runtime is shutting down, cannot create new hosts");
        }

        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        try
        {
            // Get or create the Orleans host from the service provider
            var orleansHost = _serviceProvider.GetService<IHost>();
            if (orleansHost == null)
            {
                // Create a new Orleans host if not provided
                var hostBuilder = Host.CreateDefaultBuilder()
                    .UseOrleans(builder =>
                    {
                        builder.UseLocalhostClustering();
                        if (config.Port.HasValue)
                        {
                            builder.ConfigureEndpoints(siloPort: config.Port.Value, gatewayPort: config.Port.Value + 1);
                        }
                    });
                
                orleansHost = hostBuilder.Build();
                await orleansHost.StartAsync();
            }

            // Get the grain factory
            var grainFactory = orleansHost.Services.GetRequiredService<IGrainFactory>();

            // Create the Orleans agent host
            var host = new OrleansAgentHost(
                orleansHost,
                grainFactory,
                config,
                _serviceProvider.GetService<ILogger<OrleansAgentHost>>()
            );

            // Initialize the host
            await host.StartAsync();

            // Register the host
            var hostId = Guid.NewGuid();
            if (!_hosts.TryAdd(hostId, host))
            {
                throw new InvalidOperationException($"Host with ID {hostId} already exists");
            }

            _logger.LogInformation("Created OrleansAgentHost {HostId} with name '{HostName}'", 
                hostId, config.HostName);

            return host;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Orleans agent host");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IAgentInstance> SpawnAgentAsync<TAgent>(AgentSpawnOptions options)
        where TAgent : class, new()
    {
        if (_isShuttingDown)
        {
            throw new InvalidOperationException("Runtime is shutting down, cannot spawn new agents");
        }

        try
        {
            // If no hosts exist, create a default one
            if (_hosts.IsEmpty)
            {
                var defaultConfig = new AgentHostConfiguration
                {
                    HostName = "DefaultOrleansHost"
                };
                await CreateHostAsync(defaultConfig);
            }

            // Get the first available host
            var host = _hosts.Values.FirstOrDefault();
            if (host == null)
            {
                throw new InvalidOperationException("No available Orleans hosts to spawn agent");
            }

            // Parse agent ID
            var agentId = options.AgentId ?? Guid.NewGuid().ToString();
            var agentGuid = Guid.Parse(agentId);

            // Get the grain factory from the host
            var grainFactory = host.GetGrainFactory();

            // Get the grain reference
            // Note: IGAgentGrain uses string key, so convert Guid to string
            var grain = grainFactory.GetGrain<IGAgentGrain>(agentGuid.ToString());

            // Create the agent instance wrapper
            var instance = new OrleansAgentInstance(
                agentGuid,
                typeof(TAgent).Name,
                grain,
                _serviceProvider.GetService<ILogger<OrleansAgentInstance>>()
            );

            // Initialize the instance
            await instance.InitializeAsync();

            // Set parent if specified
            if (options.ParentAgentId != null && options.AutoSubscribeToParent)
            {
                var parentGuid = Guid.Parse(options.ParentAgentId);
                await grain.SetParentAsync(parentGuid);
            }

            // Register with the host
            await host.RegisterAgentAsync(agentId, instance);

            _logger.LogInformation("Spawned Orleans agent {AgentId} of type {AgentType}",
                agentId, typeof(TAgent).Name);

            return instance;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to spawn Orleans agent of type {AgentType}", 
                typeof(TAgent).Name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsHealthyAsync()
    {
        try
        {
            if (_isShuttingDown)
            {
                return false;
            }

            // Check if all hosts are healthy
            var healthChecks = await Task.WhenAll(
                _hosts.Values.Select(h => h.IsHealthyAsync())
            );

            var allHealthy = healthChecks.All(h => h);
            
            if (!allHealthy)
            {
                _logger.LogWarning("OrleansAgentRuntime health check failed: not all hosts are healthy");
            }

            return allHealthy;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during OrleansAgentRuntime health check");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task ShutdownAsync()
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;
        _logger.LogInformation("Shutting down OrleansAgentRuntime");

        try
        {
            // Shutdown all hosts
            var shutdownTasks = _hosts.Values.Select(h => h.StopAsync());
            await Task.WhenAll(shutdownTasks);

            // Clear the hosts dictionary
            _hosts.Clear();

            _logger.LogInformation("OrleansAgentRuntime shutdown completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during OrleansAgentRuntime shutdown");
            throw;
        }
    }

    /// <summary>
    /// Gets all active hosts in the runtime.
    /// </summary>
    /// <returns>A read-only list of active hosts.</returns>
    public IReadOnlyList<IAgentHost> GetActiveHosts()
    {
        return _hosts.Values.ToList();
    }

    /// <summary>
    /// Gets a host by its ID.
    /// </summary>
    /// <param name="hostId">The unique identifier of the host.</param>
    /// <returns>The host if found; otherwise, null.</returns>
    public IAgentHost? GetHost(Guid hostId)
    {
        return _hosts.TryGetValue(hostId, out var host) ? host : null;
    }
}
