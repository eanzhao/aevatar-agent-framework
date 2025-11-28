using Aevatar.Agents;
using Aevatar.Agents.Abstractions.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Streams;

namespace Aevatar.Agents.Runtime.Orleans.CQRS;

/// <summary>
/// Interface for state projection grain
/// </summary>
public interface IStateProjectionGrain : IGrainWithStringKey
{
    /// <summary>
    /// Activate and start subscribing to state stream
    /// </summary>
    Task ActivateAsync();

    /// <summary>
    /// Get projection statistics
    /// </summary>
    Task<ProjectionStats> GetStatsAsync();
}

/// <summary>
/// Projection statistics
/// </summary>
[GenerateSerializer]
public class ProjectionStats
{
    [Id(0)] public long TotalProjected { get; set; }
    [Id(1)] public long ErrorCount { get; set; }
    [Id(2)] public DateTime LastProjectedAt { get; set; }
    [Id(3)] public string? LastAgentType { get; set; }
}

/// <summary>
/// Orleans Grain that subscribes to state streams and projects to external systems.
/// One grain per agent type for scalability.
/// </summary>
public class StateProjectionGrain : Grain, IStateProjectionGrain
{
    private readonly ILogger<StateProjectionGrain> _logger;
    private readonly StateDispatcherOptions _options;
    private readonly ProjectionStats _stats = new();
    private bool _isActivated;
    private StreamSubscriptionHandle<StateWrapper>? _subscriptionHandle;

    public StateProjectionGrain(
        ILogger<StateProjectionGrain> logger,
        IOptions<StateDispatcherOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);

        // Auto-activate on grain activation
        await ActivateAsync();
    }

    public async Task ActivateAsync()
    {
        if (_isActivated)
        {
            _logger.LogDebug("StateProjectionGrain already activated for key {Key}",
                this.GetPrimaryKeyString());
            return;
        }

        try
        {
            var agentType = this.GetPrimaryKeyString();
            _logger.LogInformation(
                "Activating StateProjectionGrain for agent type: {AgentType}",
                agentType);

            await InitializeOrResumeSubscriptionAsync(agentType);

            _isActivated = true;
            _logger.LogInformation(
                "StateProjectionGrain activated for agent type: {AgentType}",
                agentType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error activating StateProjectionGrain for key {Key}",
                this.GetPrimaryKeyString());
            throw;
        }
    }

    private async Task InitializeOrResumeSubscriptionAsync(string agentType)
    {
        var streamProvider = this.GetStreamProvider(_options.StreamProviderName);
        var streamId = StreamId.Create(_options.StreamNamespace, agentType);
        var stream = streamProvider.GetStream<StateWrapper>(streamId);

        // Get all existing subscription handles
        var handles = await stream.GetAllSubscriptionHandles();

        // Get projectors from DI
        var projectors = ServiceProvider.GetServices<IStateProjector>().ToList();
        var observer = new StateProjectionAsyncObserver(projectors, _logger, _stats);

        if (handles.Count > 0)
        {
            _logger.LogInformation(
                "Resuming {Count} subscriptions for agent type: {AgentType}",
                handles.Count, agentType);

            foreach (var handle in handles)
            {
                await handle.ResumeAsync(observer);
            }

            _subscriptionHandle = handles.First();
        }
        else
        {
            _logger.LogInformation(
                "Creating new subscription for agent type: {AgentType}",
                agentType);

            _subscriptionHandle = await stream.SubscribeAsync(observer);
        }
    }

    public Task<ProjectionStats> GetStatsAsync()
    {
        return Task.FromResult(_stats);
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct)
    {
        if (_subscriptionHandle != null)
        {
            await _subscriptionHandle.UnsubscribeAsync();
        }

        await base.OnDeactivateAsync(reason, ct);
    }
}

/// <summary>
/// Async observer that receives state changes and projects them
/// </summary>
internal class StateProjectionAsyncObserver : IAsyncObserver<StateWrapper>
{
    private readonly IReadOnlyList<IStateProjector> _projectors;
    private readonly ILogger _logger;
    private readonly ProjectionStats _stats;

    public StateProjectionAsyncObserver(
        IReadOnlyList<IStateProjector> projectors,
        ILogger logger,
        ProjectionStats stats)
    {
        _projectors = projectors;
        _logger = logger;
        _stats = stats;
    }

    public async Task OnNextAsync(StateWrapper item, StreamSequenceToken? token = null)
    {
        _logger.LogDebug(
            "Received state for agent {AgentId}, type: {AgentType}, version: {Version}",
            item.AgentId, item.AgentType, item.Version);

        foreach (var projector in _projectors)
        {
            try
            {
                await projector.ProjectAsync(item);

                _stats.TotalProjected++;
                _stats.LastProjectedAt = DateTime.UtcNow;
                _stats.LastAgentType = item.AgentType;
            }
            catch (Exception ex)
            {
                _stats.ErrorCount++;
                _logger.LogError(ex,
                    "Projector {ProjectorType} failed for agent {AgentId}",
                    projector.GetType().Name, item.AgentId);
            }
        }
    }

    public Task OnCompletedAsync()
    {
        _logger.LogInformation("State projection stream completed");
        return Task.CompletedTask;
    }

    public Task OnErrorAsync(Exception ex)
    {
        _logger.LogError(ex, "Error in state projection stream");
        return Task.CompletedTask;
    }
}

