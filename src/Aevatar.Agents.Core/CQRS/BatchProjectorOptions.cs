namespace Aevatar.Agents.Core.CQRS;

/// <summary>
/// Configuration options for batched state projection.
/// Controls batching behavior, memory management, and retry policies.
/// </summary>
public class BatchProjectorOptions
{
    /// <summary>
    /// Default batch size for processing.
    /// Commands are grouped into batches of this size before being sent to ES.
    /// </summary>
    public int BatchSize { get; set; } = 15;

    /// <summary>
    /// Batch timeout in seconds.
    /// Forces a flush even if batch size hasn't been reached.
    /// </summary>
    public int BatchTimeoutSeconds { get; set; } = 1;

    /// <summary>
    /// Maximum batch size when queue is backlogged.
    /// Used when pending commands exceed BatchSize * 5.
    /// </summary>
    public int MaxBatchSize { get; set; } = 100;

    /// <summary>
    /// Minimum batch size even under memory pressure.
    /// Ensures progress is made even when memory is constrained.
    /// </summary>
    public int MinBatchSize { get; set; } = 5;

    /// <summary>
    /// High memory threshold in bytes.
    /// When total memory exceeds this, batch size is reduced.
    /// Default: 1GB
    /// </summary>
    public long HighMemoryThreshold { get; set; } = 1024 * 1024 * 1024;

    /// <summary>
    /// Maximum retry count for failed batch operations.
    /// </summary>
    public int MaxRetryCount { get; set; } = 3;

    /// <summary>
    /// Base delay for exponential backoff retry (in seconds).
    /// Actual delay = RetryBaseDelaySeconds * 2^(retryCount-1)
    /// </summary>
    public int RetryBaseDelaySeconds { get; set; } = 2;

    /// <summary>
    /// Maximum retry delay in seconds (caps exponential backoff).
    /// </summary>
    public int MaxRetryDelaySeconds { get; set; } = 30;

    /// <summary>
    /// Minimum flush period in milliseconds.
    /// Timer checks for flush at this interval.
    /// </summary>
    public int FlushMinPeriodInMs { get; set; } = 1000;
}

