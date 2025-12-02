using Aevatar.Agents;
using Aevatar.Agents.Abstractions.CQRS;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Plugins.CQRS.Elasticsearch;

/// <summary>
/// Elasticsearch implementation of IStateProjector.
/// Directly projects state changes to Elasticsearch without going through a stream.
/// Uses StateDocumentConverter for state unpacking and conversion.
/// </summary>
public class ElasticsearchStateProjector : IStateProjector
{
    private readonly IStateIndexService _indexService;
    private readonly ILogger<ElasticsearchStateProjector> _logger;
    private readonly StateDocumentConverter _converter;

    public ElasticsearchStateProjector(
        IStateIndexService indexService,
        ILogger<ElasticsearchStateProjector> logger)
    {
        _indexService = indexService;
        _logger = logger;
        _converter = new StateDocumentConverter(logger);
    }

    public async Task ProjectAsync(StateWrapper wrapper, CancellationToken ct = default)
    {
        try
        {
            _logger.LogDebug(
                "Projecting state for {AgentId} (Type: {AgentType}, Version: {Version})",
                wrapper.AgentId, wrapper.AgentType, wrapper.Version);

            // 1. Ensure index exists (use converter to resolve type for mapping)
            var stateType = _converter.ResolveStateType(wrapper.AgentType, wrapper.StateData);
            await _indexService.EnsureIndexExistsAsync(wrapper.AgentType, stateType, ct);

            // 2. Convert to index document using shared converter
            var document = _converter.Convert(wrapper);

            // 3. Index to Elasticsearch
            await _indexService.IndexStateAsync(document, ct);

            _logger.LogDebug(
                "Successfully projected state for {AgentId}, version: {Version}",
                wrapper.AgentId, wrapper.Version);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error projecting state for {AgentId} (Type: {AgentType})",
                wrapper.AgentId, wrapper.AgentType);
            throw;
        }
    }
}
