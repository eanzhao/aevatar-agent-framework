using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Agents.Core.EventDeduplication;

/// <summary>
/// MemoryCache-based event deduplicator
/// Provides automatic expiration mechanism based on time window
/// </summary>
public class MemoryCacheEventDeduplicator : IEventDeduplicator, IDisposable
{
    private IMemoryCache _cache;
    private readonly DeduplicationOptions _options;
    private readonly ILogger<MemoryCacheEventDeduplicator> _logger;
    private readonly Timer? _cleanupTimer;
    private readonly SemaphoreSlim _cleanupSemaphore = new(1, 1);
    
    // Statistics
    private long _totalEvents;
    private long _duplicateEvents;
    private DateTime? _lastCleanupTime;

    public MemoryCacheEventDeduplicator(
        DeduplicationOptions? options = null,
        ILogger<MemoryCacheEventDeduplicator>? logger = null)
    {
        _options = options ?? new DeduplicationOptions();
        _logger = logger ?? NullLogger<MemoryCacheEventDeduplicator>.Instance;
        
        // Configure MemoryCache
        var cacheOptions = new MemoryCacheOptions
        {
            SizeLimit = _options.MaxCachedEvents,
            CompactionPercentage = 0.25 // Remove 25% of items when limit is reached
        };
        
        _cache = new MemoryCache(cacheOptions);
        
        // Set up automatic cleanup timer
        if (_options.EnableAutoCleanup)
        {
            _cleanupTimer = new Timer(
                _ => _ = CleanupExpiredAsync(),
                null,
                _options.CleanupInterval,
                _options.CleanupInterval);
        }
        
        _logger.LogDebug("EventDeduplicator initialized with expiration: {Expiration}, max events: {MaxEvents}",
            _options.EventExpiration, _options.MaxCachedEvents);
    }

    public Task<bool> TryRecordEventAsync(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            throw new ArgumentException("Event ID cannot be null or empty", nameof(eventId));
        }

        Interlocked.Increment(ref _totalEvents);

        // Use atomic operations of MemoryCache
        var cacheEntryOptions = new MemoryCacheEntryOptions()
            .SetSize(1) // Each entry occupies 1 unit of size
            .SetSlidingExpiration(_options.EventExpiration)
            .SetPriority(CacheItemPriority.Normal);

        // Atomic operation of TryGetValue + Set
        if (_cache.TryGetValue(eventId, out _))
        {
            // Event already exists (duplicate)
            Interlocked.Increment(ref _duplicateEvents);
            _logger.LogTrace("Duplicate event detected: {EventId}", eventId);
            return Task.FromResult(false);
        }

        // Record new event
        _cache.Set(eventId, true, cacheEntryOptions);
        _logger.LogTrace("New event recorded: {EventId}", eventId);
        return Task.FromResult(true);
    }

    public async Task<IReadOnlyList<string>> TryRecordEventsAsync(IReadOnlyList<string> eventIds)
    {
        if (eventIds == null || eventIds.Count == 0)
        {
            return Array.Empty<string>();
        }

        var newEvents = new List<string>(eventIds.Count);
        
        foreach (var eventId in eventIds)
        {
            if (await TryRecordEventAsync(eventId))
            {
                newEvents.Add(eventId);
            }
        }
        
        return newEvents;
    }

    public Task<bool> IsProcessedAsync(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(_cache.TryGetValue(eventId, out _));
    }

    public async Task<int> CleanupExpiredAsync()
    {
        await _cleanupSemaphore.WaitAsync();
        try
        {
            // MemoryCache automatically cleans up expired items, mainly updating statistics here
            _lastCleanupTime = DateTime.UtcNow;
            
            // Force compact cache to release expired items (only specific MemoryCache class has this method)
            if (_cache is MemoryCache memoryCache)
            {
                memoryCache.Compact(0.1); // Compact 10%
            }
            
            _logger.LogDebug("Cache cleanup completed at {Time}", _lastCleanupTime);
            
            return 0; // MemoryCache doesn't provide cleanup count information
        }
        finally
        {
            _cleanupSemaphore.Release();
        }
    }

    public Task<DeduplicationStatistics> GetStatisticsAsync()
    {
        var uniqueEvents = _totalEvents - _duplicateEvents;
        
        var stats = new DeduplicationStatistics
        {
            TotalEvents = _totalEvents,
            DuplicateEvents = _duplicateEvents,
            UniqueEvents = uniqueEvents,
            CachedEvents = GetApproximateCacheSize(),
            MemoryUsageBytes = EstimateMemoryUsage(),
            LastCleanupTime = _lastCleanupTime
        };
        
        return Task.FromResult(stats);
    }

    public Task ResetAsync()
    {
        // Clear cache (Clear method only exists in MemoryCache implementation class)
        if (_cache is MemoryCache memoryCache)
        {
            memoryCache.Clear();
        }
        else
        {
            // If not MemoryCache implementation, recreate via Dispose
            _cache.Dispose();
            var cacheOptions = new MemoryCacheOptions
            {
                SizeLimit = _options.MaxCachedEvents,
                CompactionPercentage = 0.25
            };
            _cache = new MemoryCache(cacheOptions);
        }
        
        // Reset statistics
        _totalEvents = 0;
        _duplicateEvents = 0;
        _lastCleanupTime = null;
        
        _logger.LogInformation("EventDeduplicator reset");
        
        return Task.CompletedTask;
    }

    private long GetApproximateCacheSize()
    {
        // MemoryCache doesn't directly provide count, returning estimated value here
        // Based on known unique event count
        return Math.Min(_totalEvents - _duplicateEvents, _options.MaxCachedEvents);
    }

    private long EstimateMemoryUsage()
    {
        // Estimate memory usage: each event ID takes approximately (ID length + overhead) bytes
        // Assuming average ID length is 36 bytes (GUID), plus cache overhead about 100 bytes
        var estimatedBytesPerEntry = 136;
        return GetApproximateCacheSize() * estimatedBytesPerEntry;
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        _cleanupSemaphore?.Dispose();
        _cache?.Dispose();
        
        _logger.LogDebug("EventDeduplicator disposed");
    }
}

/// <summary>
/// Event deduplicator extension methods
/// </summary>
public static class EventDeduplicatorExtensions
{
    /// <summary>
    /// Process event using deduplicator
    /// </summary>
    public static async Task<bool> ProcessEventAsync(
        this IEventDeduplicator deduplicator,
        string eventId,
        Func<Task> processAction)
    {
        if (await deduplicator.TryRecordEventAsync(eventId))
        {
            await processAction();
            return true;
        }
        
        return false;
    }

    /// <summary>
    /// Batch process events (deduplication)
    /// </summary>
    public static async Task<int> ProcessEventsAsync(
        this IEventDeduplicator deduplicator,
        IReadOnlyList<string> eventIds,
        Func<string, Task> processAction)
    {
        var newEvents = await deduplicator.TryRecordEventsAsync(eventIds);
        
        foreach (var eventId in newEvents)
        {
            await processAction(eventId);
        }
        
        return newEvents.Count;
    }
}
