using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.Runtime;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Orleans implementation of an agent host that manages agents through Orleans grains.
/// </summary>
public class OrleansAgentHost : IAgentHost
{
    private readonly IHost _orleansHost;
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<OrleansAgentHost> _logger;
    private readonly ConcurrentDictionary<string, IAgentInstance> _instances;
    private readonly AgentHostConfiguration _configuration;

    /// <inheritdoc />
    public string HostId { get; }

    /// <inheritdoc />
    public string HostName { get; }

    /// <inheritdoc />
    public string RuntimeType => "Orleans";

    /// <inheritdoc />
    public int? Port { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="OrleansAgentHost"/> class.
    /// </summary>
    /// <param name="orleansHost">The Orleans host instance.</param>
    /// <param name="grainFactory">The grain factory for creating grains.</param>
    /// <param name="configuration">The host configuration.</param>
    /// <param name="logger">The logger instance.</param>
    public OrleansAgentHost(
        IHost orleansHost,
        IGrainFactory grainFactory,
        AgentHostConfiguration configuration,
        ILogger<OrleansAgentHost>? logger = null)
    {
        _orleansHost = orleansHost ?? throw new ArgumentNullException(nameof(orleansHost));
        _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<OrleansAgentHost>.Instance;
        
        HostId = Guid.NewGuid().ToString();
        HostName = configuration.HostName;
        Port = configuration.Port;
        _instances = new ConcurrentDictionary<string, IAgentInstance>();
    }

    /// <inheritdoc />
    public async Task StartAsync()
    {
        _logger.LogInformation("Starting OrleansAgentHost {HostId}", HostId);
        
        // The Orleans host should already be started when this is called
        // This is just for consistency with the interface
        if (_orleansHost is IHostedService hostedService)
        {
            await hostedService.StartAsync(default);
        }
        
        _logger.LogInformation("OrleansAgentHost {HostId} started", HostId);
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        _logger.LogInformation("Stopping OrleansAgentHost {HostId}", HostId);

        try
        {
            // Deactivate all agent instances
            var deactivateTasks = _instances.Values.Select(i => i.DeactivateAsync());
            await Task.WhenAll(deactivateTasks);

            _instances.Clear();
            
            // Stop the Orleans host if needed
            if (_orleansHost is IHostedService hostedService)
            {
                await hostedService.StopAsync(default);
            }
            
            _logger.LogInformation("OrleansAgentHost {HostId} stopped", HostId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping OrleansAgentHost {HostId}", HostId);
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

        _logger.LogInformation("Registered agent {AgentId} in Orleans host {HostId}", agentId, HostId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UnregisterAgentAsync(string agentId)
    {
        if (_instances.TryRemove(agentId, out var agent))
        {
            _logger.LogInformation("Unregistered agent {AgentId} from Orleans host {HostId}", agentId, HostId);
        }
        else
        {
            _logger.LogWarning("Agent {AgentId} not found in Orleans host {HostId}", agentId, HostId);
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
        try
        {
            // Check if the Orleans host is running
            // You might want to add more sophisticated health checks here
            return _orleansHost != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking health of OrleansAgentHost {HostId}", HostId);
            return false;
        }
    }

    /// <summary>
    /// Gets the underlying Orleans host.
    /// </summary>
    public IHost GetOrleansHost() => _orleansHost;

    /// <summary>
    /// Gets the grain factory for creating Orleans grains.
    /// </summary>
    public IGrainFactory GetGrainFactory() => _grainFactory;
}
