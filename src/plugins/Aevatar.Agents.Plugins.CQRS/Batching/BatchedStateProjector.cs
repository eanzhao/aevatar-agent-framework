using System.Collections.Concurrent;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions.CQRS;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Plugins.CQRS.Batching;

/// <summary>
/// Batched state projector that accumulates state changes and flushes them in batches.
/// Optimized for high-throughput scenarios with configurable batch sizes and timeouts.
/// 
/// Key features:
/// - Only keeps the latest version of each agent's state
/// - Automatic flushing based on batch size or timeout
/// - Memory-aware batch sizing
/// - Exponential backoff retry on failures
/// - Timer-based periodic flush
/// - Full state unpacking (same as ElasticsearchStateProjector)
/// </summary>
public class BatchedStateProjector : IStateProjector, IAsyncDisposable, IDisposable
{
    private readonly ConcurrentDictionary<string, StateIndexDocument> _pendingDocuments = new();
    private readonly IStateIndexService _indexService;
    private readonly ILogger<BatchedStateProjector> _logger;
    private readonly BatchProjectorOptions _options;
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly Timer _flushTimer;
    private readonly StateDocumentConverter _converter;

    private int _isProcessing;
    private bool _disposed;
    private DateTime _lastFlushTime = DateTime.UtcNow;

    public BatchedStateProjector(
        IStateIndexService indexService,
        ILogger<BatchedStateProjector> logger,
        IOptions<BatchProjectorOptions> options)
    {
        _indexService = indexService;
        _logger = logger;
        _options = options.Value;
        _converter = new StateDocumentConverter(logger);

        // Initialize timer for periodic flush
        int timerPeriodMs = Math.Max(
            _options.FlushMinPeriodInMs,
            (int)(_options.BatchTimeoutSeconds * _options.FlushMinPeriodInMs / 2));

        _flushTimer = new Timer(FlushTimerCallback, null, timerPeriodMs, timerPeriodMs);
    }

    /// <summary>
    /// Constructor for manual configuration (without IOptions).
    /// </summary>
    public BatchedStateProjector(
        IStateIndexService indexService,
        ILogger<BatchedStateProjector> logger,
        BatchProjectorOptions options)
    {
        _indexService = indexService;
        _logger = logger;
        _options = options;
        _converter = new StateDocumentConverter(logger);

        int timerPeriodMs = Math.Max(
            _options.FlushMinPeriodInMs,
            (int)(_options.BatchTimeoutSeconds * _options.FlushMinPeriodInMs / 2));

        _flushTimer = new Timer(FlushTimerCallback, null, timerPeriodMs, timerPeriodMs);
    }

    public Task ProjectAsync(StateWrapper wrapper, CancellationToken ct = default)
    {
        if (_disposed)
        {
            _logger.LogWarning("ProjectAsync called after disposal");
            return Task.CompletedTask;
        }

        try
        {
            _logger.LogDebug(
                "BatchedStateProjector received state for {AgentId} (Version: {Version})",
                wrapper.AgentId, wrapper.Version);

            // Convert to index document
            var document = ConvertToDocument(wrapper);

            // Update pending documents - only keep latest version
            _pendingDocuments.AddOrUpdate(
                wrapper.AgentId,
                _ => document,
                (_, existing) => document.Version > existing.Version ? document : existing
            );

            // Check if flush is needed
            var shouldFlush = _pendingDocuments.Count >= _options.BatchSize ||
                              (DateTime.UtcNow - _lastFlushTime).TotalSeconds >= _options.BatchTimeoutSeconds;

            if (shouldFlush && Interlocked.CompareExchange(ref _isProcessing, 1, 0) == 0)
            {
                // Non-blocking flush execution
                return FlushInternalAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in BatchedStateProjector.ProjectAsync for {AgentId}", wrapper.AgentId);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Force flush all pending documents.
    /// </summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        if (_pendingDocuments.IsEmpty)
        {
            return;
        }

        try
        {
            // Calculate effective batch size based on queue and memory
            int effectiveBatchSize = CalculateEffectiveBatchSize();

            // Get batch ordered by version (highest first) then by indexed time
            var currentBatch = _pendingDocuments.Values
                .OrderByDescending(d => d.Version)
                .ThenByDescending(d => d.IndexedAt)
                .Take(effectiveBatchSize)
                .ToList();

            if (currentBatch.Count > 0)
            {
                _logger.LogInformation(
                    "Processing batch: {BatchSize} documents (total pending: {TotalCount})",
                    currentBatch.Count, _pendingDocuments.Count);

                await SendBatchWithRetryAsync(currentBatch, ct);

                // Remove processed documents (only if version matches)
                foreach (var doc in currentBatch)
                {
                    if (_pendingDocuments.TryGetValue(doc.AgentId, out var currentDoc))
                    {
                        if (currentDoc.Version == doc.Version)
                        {
                            _pendingDocuments.TryRemove(doc.AgentId, out _);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch processing failed");
            throw;
        }
    }

    /// <summary>
    /// Get count of pending documents.
    /// </summary>
    public int PendingCount => _pendingDocuments.Count;

    #region Private Methods

    private async Task FlushInternalAsync(CancellationToken ct)
    {
        try
        {
            await FlushAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during flush operation");
        }
        finally
        {
            Interlocked.Exchange(ref _isProcessing, 0);
            _lastFlushTime = DateTime.UtcNow;
        }
    }

    private int CalculateEffectiveBatchSize()
    {
        int size = _options.BatchSize;

        // Increase batch size if queue is backlogged
        if (_pendingDocuments.Count > _options.BatchSize * 5)
        {
            size = Math.Min(_options.BatchSize * 2, _options.MaxBatchSize);
        }

        // Reduce batch size under memory pressure
        if (GC.GetTotalMemory(false) > _options.HighMemoryThreshold)
        {
            size = Math.Max(_options.MinBatchSize, size / 2);
            _logger.LogWarning(
                "High memory pressure detected, reducing batch size to {BatchSize}",
                size);
        }

        return size;
    }

    private async Task SendBatchWithRetryAsync(List<StateIndexDocument> batch, CancellationToken ct)
    {
        int retryCount = 0;

        while (retryCount < _options.MaxRetryCount && !ct.IsCancellationRequested)
        {
            try
            {
                // Ensure indices exist for all agent types in batch
                var agentTypes = batch.Select(d => d.AgentType).Distinct();
                foreach (var agentType in agentTypes)
                {
                    await _indexService.EnsureIndexExistsAsync(agentType, null, ct);
                }

                // Send batch to index service
                await _indexService.IndexStateBatchAsync(batch, ct);
                
                _logger.LogDebug(
                    "Successfully indexed batch of {Count} documents",
                    batch.Count);
                return;
            }
            catch (Exception ex)
            {
                retryCount++;

                if (retryCount >= _options.MaxRetryCount)
                {
                    _logger.LogError(ex,
                        "Failed to process batch after {RetryCount} attempts",
                        retryCount);
                    throw;
                }

                _logger.LogWarning(ex,
                    "Error processing batch, will retry ({RetryCount}/{MaxRetries})",
                    retryCount, _options.MaxRetryCount);

                // Exponential backoff with cap
                int delayMs = (int)Math.Min(
                    _options.RetryBaseDelaySeconds * 1000 * Math.Pow(2, retryCount - 1),
                    _options.MaxRetryDelaySeconds * 1000);

                await Task.Delay(delayMs, ct);
            }
        }
    }

    private StateIndexDocument ConvertToDocument(StateWrapper wrapper)
    {
        // Use shared converter for full state unpacking
        // Same logic as ElasticsearchStateProjector
        return _converter.Convert(wrapper);
    }

    private void FlushTimerCallback(object? state)
    {
        if (_disposed) return;

        if (!_pendingDocuments.IsEmpty && Interlocked.CompareExchange(ref _isProcessing, 1, 0) == 0)
        {
            try
            {
                // Non-blocking flush execution
                _ = FlushInternalAsync(_shutdownCts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Timer] Exception in FlushTimerCallback");
                Interlocked.Exchange(ref _isProcessing, 0);
            }
        }
    }

    #endregion

    #region IDisposable / IAsyncDisposable

    /// <summary>
    /// Async dispose - preferred method to avoid potential deadlocks.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Stop timer
        await _flushTimer.DisposeAsync();

        // Try final flush with timeout
        if (_pendingDocuments.Count > 0 && Interlocked.CompareExchange(ref _isProcessing, 1, 0) == 0)
        {
            try
            {
                _logger.LogInformation(
                    "Disposing BatchedStateProjector, flushing {Count} pending documents",
                    _pendingDocuments.Count);
                
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await FlushAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Final flush timed out during dispose");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during final flush on dispose");
            }
        }

        await _shutdownCts.CancelAsync();
        _shutdownCts.Dispose();
        
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Sync dispose - calls async dispose safely.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        
        // Use a separate task to avoid deadlock
        Task.Run(async () => await DisposeAsync()).GetAwaiter().GetResult();
    }

    #endregion
}

