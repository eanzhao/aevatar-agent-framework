using Aevatar.Agents;
using Aevatar.Agents.Abstractions.CQRS;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Plugins.CQRS.Logging;

/// <summary>
/// Logging-only state projector for development and debugging.
/// Logs state changes without persisting them anywhere.
/// </summary>
public class LoggingStateProjector : IStateProjector
{
    private readonly ILogger<LoggingStateProjector> _logger;
    private readonly StateDocumentConverter _converter;

    public LoggingStateProjector(ILogger<LoggingStateProjector> logger)
    {
        _logger = logger;
        _converter = new StateDocumentConverter(logger);
    }

    public Task ProjectAsync(StateWrapper wrapper, CancellationToken ct = default)
    {
        try
        {
            var document = _converter.Convert(wrapper);

            _logger.LogInformation(
                "📋 [LoggingProjector] Agent: {AgentId} ({AgentType}), Version: {Version}, Data: {Data}",
                wrapper.AgentId,
                wrapper.AgentType,
                wrapper.Version,
                document.Data != null ? string.Join(", ", document.Data.Select(kv => $"{kv.Key}={kv.Value}")) : "null");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in LoggingStateProjector for {AgentId}", wrapper.AgentId);
        }

        return Task.CompletedTask;
    }
}

