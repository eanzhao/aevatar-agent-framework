using Aevatar.Agents;
using Aevatar.Agents.Abstractions.CQRS;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Plugins.CQRS;

/// <summary>
/// Composite state projector that delegates to multiple underlying projectors.
/// Useful for sending state changes to multiple destinations (e.g., ES + logging).
/// </summary>
public class CompositeStateProjector : IStateProjector
{
    private readonly IEnumerable<IStateProjector> _projectors;
    private readonly ILogger<CompositeStateProjector> _logger;

    public CompositeStateProjector(
        IEnumerable<IStateProjector> projectors,
        ILogger<CompositeStateProjector> logger)
    {
        _projectors = projectors ?? throw new ArgumentNullException(nameof(projectors));
        _logger = logger;
    }

    public async Task ProjectAsync(StateWrapper wrapper, CancellationToken ct = default)
    {
        var projectorList = _projectors.ToList();
        _logger.LogDebug(
            "CompositeStateProjector dispatching to {Count} projectors for {AgentId}",
            projectorList.Count, wrapper.AgentId);

        var tasks = projectorList.Select(async p =>
        {
            try
            {
                await p.ProjectAsync(wrapper, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error in composite projector component {ProjectorType} for {AgentId}",
                    p.GetType().Name, wrapper.AgentId);
            }
        });

        await Task.WhenAll(tasks);
    }
}

