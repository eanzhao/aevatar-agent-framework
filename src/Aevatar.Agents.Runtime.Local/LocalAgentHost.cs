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
using Google.Protobuf;

namespace Aevatar.Agents.Runtime.Local;

/// <summary>
/// Local implementation of an agent host that manages agents in-memory.
/// </summary>
public class LocalAgentHost : IAgentHost
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LocalAgentHost> _logger;
    private readonly ConcurrentDictionary<string, IAgentInstance> _instances;
    private readonly AgentHostConfiguration _configuration;
    private volatile bool _isRunning;

    /// <inheritdoc />
    public string HostId { get; }

    /// <inheritdoc />
    public string HostName { get; }

    /// <inheritdoc />
    public string RuntimeType => "Local";

    /// <inheritdoc />
    public int? Port => null; // Local runtime doesn't use ports

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalAgentHost"/> class.
    /// </summary>
    /// <param name="configuration">The host configuration.</param>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="logger">The logger instance.</param>
    public LocalAgentHost(
        AgentHostConfiguration configuration,
        IServiceProvider serviceProvider,
        ILogger<LocalAgentHost>? logger = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<LocalAgentHost>.Instance;
        
        HostId = Guid.NewGuid().ToString();
        HostName = configuration.HostName;
        _instances = new ConcurrentDictionary<string, IAgentInstance>();
    }

    /// <inheritdoc />
    public Task StartAsync()
    {
        if (_isRunning)
        {
        _logger.LogWarning("LocalAgentHost {HostId} is already running", HostId);
        return Task.CompletedTask;
        }

        _isRunning = true;
        _logger.LogInformation("LocalAgentHost {HostId} started", HostId);
        
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        if (!_isRunning)
        {
            _logger.LogWarning("LocalAgentHost {HostId} is not running", HostId);
            return;
        }

        _isRunning = false;
        _logger.LogInformation("Stopping LocalAgentHost {HostId}", HostId);

        try
        {
            // Deactivate all agent instances
            var deactivateTasks = _instances.Values.Select(i => i.DeactivateAsync());
            await Task.WhenAll(deactivateTasks);

            _instances.Clear();
            _logger.LogInformation("LocalAgentHost {HostId} stopped", HostId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping LocalAgentHost {HostId}", HostId);
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

        _logger.LogInformation("Registered agent {AgentId} in host {HostId}", agentId, HostId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UnregisterAgentAsync(string agentId)
    {
        if (_instances.TryRemove(agentId, out var agent))
        {
            _logger.LogInformation("Unregistered agent {AgentId} from host {HostId}", agentId, HostId);
        }
        else
        {
            _logger.LogWarning("Agent {AgentId} not found in host {HostId}", agentId, HostId);
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
            // For local host, we consider it healthy if it's running
            // and has no critical errors
            return _isRunning;

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking health of LocalAgentHost {HostId}", HostId);
            return false;
        }
    }

}
