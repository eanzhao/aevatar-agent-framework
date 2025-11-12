using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Proto;
using Proto.DependencyInjection;

namespace Aevatar.Agents.Runtime.ProtoActor;

/// <summary>
/// ProtoActor runtime implementation for high-performance agent execution.
/// Provides actor-based concurrency and optional distributed deployment.
/// </summary>
public class ProtoActorAgentRuntime : IAgentRuntime
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProtoActorAgentRuntime> _logger;
    private readonly ConcurrentDictionary<Guid, ProtoActorAgentHost> _hosts;
    private volatile bool _isShuttingDown;

    /// <inheritdoc />
    public string RuntimeType => "ProtoActor";

    /// <summary>
    /// Initializes a new instance of the <see cref="ProtoActorAgentRuntime"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider for dependency injection.</param>
    /// <param name="logger">The logger instance.</param>
    public ProtoActorAgentRuntime(IServiceProvider serviceProvider, ILogger<ProtoActorAgentRuntime>? logger = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ProtoActorAgentRuntime>.Instance;
        _hosts = new ConcurrentDictionary<Guid, ProtoActorAgentHost>();
        
        _logger.LogInformation("ProtoActorAgentRuntime initialized");
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
            // Get or create the ActorSystem from the service provider
            var actorSystem = _serviceProvider.GetService<ActorSystem>();
            if (actorSystem == null)
            {
                // Create a new ActorSystem if not provided
                var systemConfig = ActorSystemConfig.Setup()
                    .WithDeadLetterThrottleCount(10)
                    .WithDeadLetterThrottleInterval(TimeSpan.FromSeconds(1));
                
                actorSystem = new ActorSystem(systemConfig)
                    .WithServiceProvider(_serviceProvider);
            }

            // Create the ProtoActor agent host
            var host = new ProtoActorAgentHost(
                actorSystem,
                config,
                _serviceProvider.GetService<ILogger<ProtoActorAgentHost>>()
            );

            // Initialize the host
            await host.StartAsync();

            // Register the host
            var hostId = Guid.NewGuid();
            if (!_hosts.TryAdd(hostId, host))
            {
                throw new InvalidOperationException($"Host with ID {hostId} already exists");
            }

            _logger.LogInformation("Created ProtoActorAgentHost {HostId} with name '{HostName}'", 
                hostId, config.HostName);

            return host;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create ProtoActor agent host");
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
                    HostName = "DefaultProtoActorHost"
                };
                await CreateHostAsync(defaultConfig);
            }

            // Get the first available host
            var host = _hosts.Values.FirstOrDefault();
            if (host == null)
            {
                throw new InvalidOperationException("No available ProtoActor hosts to spawn agent");
            }

            // Parse agent ID
            var agentId = options.AgentId ?? Guid.NewGuid().ToString();
            var agentGuid = Guid.Parse(agentId);

            // Get the actor system and root context from the host
            var actorSystem = host.GetActorSystem();
            var rootContext = host.GetRootContext();

            // Get the actor factory and manager from service provider
            var actorFactory = _serviceProvider.GetService<ProtoActorGAgentActorFactory>();
            var actorManager = _serviceProvider.GetService<ProtoActorGAgentActorManager>();
            var streamRegistry = _serviceProvider.GetService<ProtoActorMessageStreamRegistry>();

            if (actorFactory == null || actorManager == null || streamRegistry == null)
            {
                throw new InvalidOperationException("Required ProtoActor services not registered in DI");
            }

            // Check if TAgent implements IGAgent at runtime
            if (!typeof(IGAgent).IsAssignableFrom(typeof(TAgent)))
            {
                throw new InvalidOperationException($"Agent type {typeof(TAgent).Name} must implement IGAgent");
            }

            // Create the agent actor through the manager
            var createMethod = actorManager.GetType()
                .GetMethod(nameof(ProtoActorGAgentActorManager.CreateAndRegisterAsync))
                ?.MakeGenericMethod(typeof(TAgent));
            
            if (createMethod == null)
            {
                throw new InvalidOperationException("Could not find CreateAndRegisterAsync method");
            }

            var actorTask = createMethod.Invoke(actorManager, new object?[] { agentGuid, default(System.Threading.CancellationToken) }) as Task<IGAgentActor>;
            if (actorTask == null)
            {
                throw new InvalidOperationException("Failed to invoke CreateAndRegisterAsync");
            }
            
            var actor = await actorTask as ProtoActorGAgentActor;
            if (actor == null)
            {
                throw new InvalidOperationException("Created actor is not a ProtoActorGAgentActor");
            }

            // Get the PID from the actor
            var pid = actor.GetPid();

            // Create the agent instance wrapper
            var instance = new ProtoActorAgentInstance(
                agentGuid,
                typeof(TAgent).Name,
                actor,
                rootContext,
                pid,
                _serviceProvider.GetService<ILogger<ProtoActorAgentInstance>>()
            );

            // Initialize the instance
            await instance.InitializeAsync();

            // Set parent if specified
            if (options.ParentAgentId != null && options.AutoSubscribeToParent)
            {
                var parentGuid = Guid.Parse(options.ParentAgentId);
                await actor.SetParentAsync(parentGuid);
            }

            // Register with the host
            await host.RegisterAgentAsync(agentId, instance);

            _logger.LogInformation("Spawned ProtoActor agent {AgentId} of type {AgentType}",
                agentId, typeof(TAgent).Name);

            return instance;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to spawn ProtoActor agent of type {AgentType}", 
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
                _logger.LogWarning("ProtoActorAgentRuntime health check failed: not all hosts are healthy");
            }

            return allHealthy;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during ProtoActorAgentRuntime health check");
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
        _logger.LogInformation("Shutting down ProtoActorAgentRuntime");

        try
        {
            // Shutdown all hosts
            var shutdownTasks = _hosts.Values.Select(h => h.StopAsync());
            await Task.WhenAll(shutdownTasks);

            // Clear the hosts dictionary
            _hosts.Clear();

            _logger.LogInformation("ProtoActorAgentRuntime shutdown completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during ProtoActorAgentRuntime shutdown");
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
