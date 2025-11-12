using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.Runtime;
using Microsoft.Extensions.Logging;
using Proto;

namespace Aevatar.Agents.Runtime.ProtoActor;

/// <summary>
/// ProtoActor implementation of an agent host that manages agents through Proto.Actor system.
/// </summary>
public class ProtoActorAgentHost : IAgentHost
{
    private readonly ActorSystem _actorSystem;
    private readonly IRootContext _rootContext;
    private readonly ILogger<ProtoActorAgentHost> _logger;
    private readonly ConcurrentDictionary<string, IAgentInstance> _instances;
    private readonly AgentHostConfiguration _configuration;
    private volatile bool _isRunning;

    /// <inheritdoc />
    public string HostId { get; }

    /// <inheritdoc />
    public string HostName { get; }

    /// <inheritdoc />
    public string RuntimeType => "ProtoActor";

    /// <inheritdoc />
    public int? Port { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProtoActorAgentHost"/> class.
    /// </summary>
    /// <param name="actorSystem">The ProtoActor system instance.</param>
    /// <param name="configuration">The host configuration.</param>
    /// <param name="logger">The logger instance.</param>
    public ProtoActorAgentHost(
        ActorSystem actorSystem,
        AgentHostConfiguration configuration,
        ILogger<ProtoActorAgentHost>? logger = null)
    {
        _actorSystem = actorSystem ?? throw new ArgumentNullException(nameof(actorSystem));
        _rootContext = actorSystem.Root;
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ProtoActorAgentHost>.Instance;
        
        HostId = Guid.NewGuid().ToString();
        HostName = configuration.HostName;
        Port = configuration.Port;
        _instances = new ConcurrentDictionary<string, IAgentInstance>();
    }

    /// <inheritdoc />
    public Task StartAsync()
    {
        if (_isRunning)
        {
            _logger.LogWarning("ProtoActorAgentHost {HostId} is already running", HostId);
            return Task.CompletedTask;
        }

        _isRunning = true;
        _logger.LogInformation("ProtoActorAgentHost {HostId} started", HostId);
        
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        if (!_isRunning)
        {
            _logger.LogWarning("ProtoActorAgentHost {HostId} is not running", HostId);
            return;
        }

        _isRunning = false;
        _logger.LogInformation("Stopping ProtoActorAgentHost {HostId}", HostId);

        try
        {
            // Deactivate all agent instances
            var deactivateTasks = _instances.Values.Select(i => i.DeactivateAsync());
            await Task.WhenAll(deactivateTasks);

            _instances.Clear();
            
            // Shutdown the actor system
            await _actorSystem.ShutdownAsync();
            
            _logger.LogInformation("ProtoActorAgentHost {HostId} stopped", HostId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping ProtoActorAgentHost {HostId}", HostId);
            throw;
        }
    }

    /// <inheritdoc />
    public Task RegisterAgentAsync(string agentId, IAgentInstance agent)
    {
        if (agent == null)
        {
            throw new ArgumentNullException(nameof(agent));
        }

        if (string.IsNullOrEmpty(agentId))
        {
            throw new ArgumentException("Agent ID cannot be null or empty", nameof(agentId));
        }

        if (!_instances.TryAdd(agentId, agent))
        {
            throw new InvalidOperationException($"Agent with ID {agentId} already registered");
        }

        _logger.LogInformation("Registered agent {AgentId} in ProtoActor host {HostId}", agentId, HostId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UnregisterAgentAsync(string agentId)
    {
        if (_instances.TryRemove(agentId, out var agent))
        {
            _logger.LogInformation("Unregistered agent {AgentId} from ProtoActor host {HostId}", agentId, HostId);
        }
        else
        {
            _logger.LogWarning("Agent {AgentId} not found in ProtoActor host {HostId}", agentId, HostId);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IAgentInstance?> GetAgentAsync(string agentId)
    {
        if (_instances.TryGetValue(agentId, out var agent))
        {
            return Task.FromResult<IAgentInstance?>(agent);
        }
        return Task.FromResult<IAgentInstance?>(null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetAgentIdsAsync()
    {
        var ids = _instances.Keys.ToList();
        return Task.FromResult<IReadOnlyList<string>>(ids);
    }

    /// <inheritdoc />
    public async Task<bool> IsHealthyAsync()
    {
        if (!_isRunning)
        {
            return false;
        }

        try
        {
            // For ProtoActor, we consider it healthy if the actor system is running
            return _actorSystem != null && _isRunning;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking health of ProtoActorAgentHost {HostId}", HostId);
            return false;
        }
    }

    /// <summary>
    /// Gets the underlying actor system.
    /// </summary>
    public ActorSystem GetActorSystem() => _actorSystem;

    /// <summary>
    /// Gets the root context for creating actors.
    /// </summary>
    public IRootContext GetRootContext() => _rootContext;
}
