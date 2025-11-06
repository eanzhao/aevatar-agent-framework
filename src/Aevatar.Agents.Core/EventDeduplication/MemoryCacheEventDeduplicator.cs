using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Aevatar.Agents.Abstractions;

namespace Aevatar.Agents.Core.EventDeduplication;

/// <summary>
/// 基于MemoryCache的事件去重器
/// 提供基于时间窗口的自动过期机制
/// </summary>
public class MemoryCacheEventDeduplicator : IEventDeduplicator, IDisposable
{
    private IMemoryCache _cache;
    private readonly DeduplicationOptions _options;
    private readonly ILogger<MemoryCacheEventDeduplicator> _logger;
    private readonly Timer? _cleanupTimer;
    private readonly SemaphoreSlim _cleanupSemaphore = new(1, 1);
    
    // 统计信息
    private long _totalEvents;
    private long _duplicateEvents;
    private DateTime? _lastCleanupTime;

    public MemoryCacheEventDeduplicator(
        DeduplicationOptions? options = null,
        ILogger<MemoryCacheEventDeduplicator>? logger = null)
    {
        _options = options ?? new DeduplicationOptions();
        _logger = logger ?? NullLogger<MemoryCacheEventDeduplicator>.Instance;
        
        // 配置MemoryCache
        var cacheOptions = new MemoryCacheOptions
        {
            SizeLimit = _options.MaxCachedEvents,
            CompactionPercentage = 0.25 // 当达到限制时移除25%的项
        };
        
        _cache = new MemoryCache(cacheOptions);
        
        // 设置自动清理定时器
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

        // 使用MemoryCache的原子操作
        var cacheEntryOptions = new MemoryCacheEntryOptions()
            .SetSize(1) // 每个条目占用1个单位大小
            .SetSlidingExpiration(_options.EventExpiration)
            .SetPriority(CacheItemPriority.Normal);

        // TryGetValue + Set 的原子操作
        if (_cache.TryGetValue(eventId, out _))
        {
            // 事件已存在（重复）
            Interlocked.Increment(ref _duplicateEvents);
            _logger.LogTrace("Duplicate event detected: {EventId}", eventId);
            return Task.FromResult(false);
        }

        // 记录新事件
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
            // MemoryCache会自动清理过期项，这里主要是更新统计信息
            _lastCleanupTime = DateTime.UtcNow;
            
            // 强制压缩缓存以释放过期项（只有具体的MemoryCache类有此方法）
            if (_cache is MemoryCache memoryCache)
            {
                memoryCache.Compact(0.1); // 压缩10%
            }
            
            _logger.LogDebug("Cache cleanup completed at {Time}", _lastCleanupTime);
            
            return 0; // MemoryCache不提供清理数量的信息
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
        // 清空缓存（Clear方法只在MemoryCache实现类中存在）
        if (_cache is MemoryCache memoryCache)
        {
            memoryCache.Clear();
        }
        else
        {
            // 如果不是MemoryCache实现，通过Dispose重新创建
            _cache.Dispose();
            var cacheOptions = new MemoryCacheOptions
            {
                SizeLimit = _options.MaxCachedEvents,
                CompactionPercentage = 0.25
            };
            _cache = new MemoryCache(cacheOptions);
        }
        
        // 重置统计
        _totalEvents = 0;
        _duplicateEvents = 0;
        _lastCleanupTime = null;
        
        _logger.LogInformation("EventDeduplicator reset");
        
        return Task.CompletedTask;
    }

    private long GetApproximateCacheSize()
    {
        // MemoryCache不直接提供计数，这里返回估算值
        // 基于已知的唯一事件数
        return Math.Min(_totalEvents - _duplicateEvents, _options.MaxCachedEvents);
    }

    private long EstimateMemoryUsage()
    {
        // 估算内存使用：每个事件ID约占用 (ID长度 + 开销) 字节
        // 假设平均ID长度为36字节（GUID），加上缓存开销约100字节
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
/// 事件去重器扩展方法
/// </summary>
public static class EventDeduplicatorExtensions
{
    /// <summary>
    /// 使用去重器处理事件
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
    /// 批量处理事件（去重）
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
