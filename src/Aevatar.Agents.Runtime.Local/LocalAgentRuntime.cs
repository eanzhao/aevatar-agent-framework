using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Runtime.Local;

/// <summary>
/// Local runtime implementation for in-memory agent execution.
/// Provides zero-configuration local development and testing.
/// </summary>
public class LocalAgentRuntime : IAgentRuntime
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LocalAgentRuntime> _logger;
    private readonly ConcurrentDictionary<Guid, LocalAgentHost> _hosts;
    private volatile bool _isShuttingDown;

    /// <inheritdoc />
    public string RuntimeType => "Local";

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalAgentRuntime"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider for dependency injection.</param>
    /// <param name="logger">The logger instance.</param>
    public LocalAgentRuntime(IServiceProvider serviceProvider, ILogger<LocalAgentRuntime>? logger = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<LocalAgentRuntime>.Instance;
        _hosts = new ConcurrentDictionary<Guid, LocalAgentHost>();
        
        _logger.LogInformation("LocalAgentRuntime initialized");
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

        // Create a new Local host with the provided configuration
        var host = new LocalAgentHost(
            config,
            _serviceProvider,
            _serviceProvider.GetService<ILogger<LocalAgentHost>>()
        );

        // Initialize the host
        await host.StartAsync();

        // Register the host  
        var hostId = Guid.NewGuid();
        if (!_hosts.TryAdd(hostId, host))
        {
            throw new InvalidOperationException($"Host with ID {hostId} already exists");
        }

        _logger.LogInformation("Created LocalAgentHost {HostId} with name '{HostName}'", 
            hostId, config.HostName);

        return host;
    }

    /// <inheritdoc />
    public async Task<IAgentInstance> SpawnAgentAsync<TAgent>(AgentSpawnOptions options)
        where TAgent : class, new()
    {
        if (_isShuttingDown)
        {
            throw new InvalidOperationException("Runtime is shutting down, cannot spawn new agents");
        }

        // If no hosts exist, create a default one
        if (_hosts.IsEmpty)
        {
            var defaultConfig = new AgentHostConfiguration
            {
                HostName = "DefaultLocalHost"
            };
            await CreateHostAsync(defaultConfig);
        }

        // Create and register the agent instance
        var agentId = options.AgentId ?? Guid.NewGuid().ToString();
        var agentGuid = Guid.Parse(agentId);
        
        // Get the actor manager from service provider
        var actorManager = _serviceProvider.GetService<LocalGAgentActorManager>()
            ?? throw new InvalidOperationException("LocalGAgentActorManager not registered in DI");
        
        // Check if TAgent implements IGAgent at runtime
        if (!typeof(IGAgent).IsAssignableFrom(typeof(TAgent)))
        {
            throw new InvalidOperationException($"Agent type {typeof(TAgent).Name} must implement IGAgent");
        }

        // Create the actor through the manager using dynamic method invocation
        var createMethod = actorManager.GetType()
            .GetMethod(nameof(LocalGAgentActorManager.CreateAndRegisterAsync))
            ?.MakeGenericMethod(typeof(TAgent));
        
        if (createMethod == null)
        {
            throw new InvalidOperationException("Could not find CreateAndRegisterAsync method");
        }

        var actorTask = createMethod.Invoke(actorManager, new object?[] { agentGuid, default(CancellationToken) }) as Task<IGAgentActor>;
        if (actorTask == null)
        {
            throw new InvalidOperationException("Failed to invoke CreateAndRegisterAsync");
        }
        
        var actor = await actorTask;
        
        // Create instance wrapper
        var instance = new LocalAgentInstance(
            agentGuid,
            typeof(TAgent).Name,
            actor,
            _serviceProvider.GetService<ILogger<LocalAgentInstance>>()
        );

        // Initialize the instance
        await instance.InitializeAsync();

        // Register with the first available host
        var host = _hosts.Values.FirstOrDefault();
        if (host == null)
        {
            throw new InvalidOperationException("No available hosts to spawn agent");
        }

        await host.RegisterAgentAsync(agentId, instance);

        return instance;
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
                _logger.LogWarning("LocalAgentRuntime health check failed: not all hosts are healthy");
            }

            return allHealthy;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during LocalAgentRuntime health check");
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
        _logger.LogInformation("Shutting down LocalAgentRuntime");

        try
        {
            // Shutdown all hosts
            var shutdownTasks = _hosts.Values.Select(h => h.StopAsync());
            await Task.WhenAll(shutdownTasks);

            // Clear the hosts dictionary
            _hosts.Clear();

            _logger.LogInformation("LocalAgentRuntime shutdown completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during LocalAgentRuntime shutdown");
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

    /// <summary>
    /// Removes a host from the runtime.
    /// </summary>
    /// <param name="hostId">The ID of the host to remove.</param>
    /// <returns>True if the host was removed; otherwise, false.</returns>
    public async Task<bool> RemoveHostAsync(Guid hostId)
    {
        if (_hosts.TryRemove(hostId, out var host))
        {
            await host.StopAsync();
            _logger.LogInformation("Removed LocalAgentHost {HostId}", hostId);
            return true;
        }
        return false;
    }
}
