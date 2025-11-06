using Xunit;
using Aevatar.Agents.Core.EventDeduplication;
using Aevatar.Agents.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace Aevatar.Agents.Core.Tests.EventDeduplication;

public class MemoryCacheEventDeduplicatorTests
{
    [Fact]
    public async Task TryRecordEventAsync_ShouldReturnTrue_ForNewEvent()
    {
        // Arrange
        var deduplicator = new MemoryCacheEventDeduplicator(new DeduplicationOptions
        {
            EventExpiration = TimeSpan.FromMinutes(5),
            MaxCachedEvents = 100
        });
        
        // Act
        var result = await deduplicator.TryRecordEventAsync("event-1");
        
        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task TryRecordEventAsync_ShouldReturnFalse_ForDuplicateEvent()
    {
        // Arrange
        var deduplicator = new MemoryCacheEventDeduplicator(new DeduplicationOptions
        {
            EventExpiration = TimeSpan.FromMinutes(5),
            MaxCachedEvents = 100
        });
        
        // Act
        await deduplicator.TryRecordEventAsync("event-1");
        var result = await deduplicator.TryRecordEventAsync("event-1");
        
        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task TryRecordEventsAsync_ShouldReturnOnlyNewEvents()
    {
        // Arrange
        var deduplicator = new MemoryCacheEventDeduplicator(new DeduplicationOptions
        {
            EventExpiration = TimeSpan.FromMinutes(5),
            MaxCachedEvents = 100
        });
        
        // 预先记录一些事件
        await deduplicator.TryRecordEventAsync("event-1");
        await deduplicator.TryRecordEventAsync("event-2");
        
        // Act
        var eventIds = new List<string> { "event-1", "event-2", "event-3", "event-4" };
        var newEvents = await deduplicator.TryRecordEventsAsync(eventIds);
        
        // Assert
        Assert.Equal(2, newEvents.Count);
        Assert.Contains("event-3", newEvents);
        Assert.Contains("event-4", newEvents);
        Assert.DoesNotContain("event-1", newEvents);
        Assert.DoesNotContain("event-2", newEvents);
    }

    [Fact]
    public async Task IsProcessedAsync_ShouldReturnCorrectStatus()
    {
        // Arrange
        var deduplicator = new MemoryCacheEventDeduplicator(new DeduplicationOptions
        {
            EventExpiration = TimeSpan.FromMinutes(5),
            MaxCachedEvents = 100
        });
        
        await deduplicator.TryRecordEventAsync("event-1");
        
        // Act
        var isProcessed1 = await deduplicator.IsProcessedAsync("event-1");
        var isProcessed2 = await deduplicator.IsProcessedAsync("event-2");
        
        // Assert
        Assert.True(isProcessed1);
        Assert.False(isProcessed2);
    }

    [Fact]
    public async Task EventExpiration_ShouldRemoveExpiredEvents()
    {
        // Arrange
        var deduplicator = new MemoryCacheEventDeduplicator(new DeduplicationOptions
        {
            EventExpiration = TimeSpan.FromMilliseconds(100), // 短过期时间
            MaxCachedEvents = 100
        });
        
        // Act
        await deduplicator.TryRecordEventAsync("event-1");
        
        // 等待事件过期
        await Task.Delay(150);
        
        var isProcessed = await deduplicator.IsProcessedAsync("event-1");
        var canRecordAgain = await deduplicator.TryRecordEventAsync("event-1");
        
        // Assert
        Assert.False(isProcessed); // 应该已经过期
        Assert.True(canRecordAgain); // 应该能够重新记录
    }

    [Fact]
    public async Task GetStatisticsAsync_ShouldReturnCorrectStats()
    {
        // Arrange
        var deduplicator = new MemoryCacheEventDeduplicator(new DeduplicationOptions
        {
            EventExpiration = TimeSpan.FromMinutes(5),
            MaxCachedEvents = 100
        });
        
        // Act
        // 记录一些事件
        await deduplicator.TryRecordEventAsync("event-1");
        await deduplicator.TryRecordEventAsync("event-2");
        await deduplicator.TryRecordEventAsync("event-3");
        
        // 尝试重复
        await deduplicator.TryRecordEventAsync("event-1");
        await deduplicator.TryRecordEventAsync("event-2");
        
        var stats = await deduplicator.GetStatisticsAsync();
        
        // Assert
        Assert.Equal(3, stats.UniqueEvents);
        Assert.Equal(2, stats.DuplicateEvents);
        Assert.Equal(5, stats.TotalEvents);
        Assert.True(stats.CachedEvents > 0);
    }

    [Fact]
    public async Task ResetAsync_ShouldClearAllEvents()
    {
        // Arrange
        var deduplicator = new MemoryCacheEventDeduplicator(new DeduplicationOptions
        {
            EventExpiration = TimeSpan.FromMinutes(5),
            MaxCachedEvents = 100
        });
        
        // 记录事件
        await deduplicator.TryRecordEventAsync("event-1");
        await deduplicator.TryRecordEventAsync("event-2");
        
        // Act
        await deduplicator.ResetAsync();
        
        // Assert
        var stats = await deduplicator.GetStatisticsAsync();
        Assert.Equal(0, stats.UniqueEvents);
        
        // 应该能够重新记录相同的事件
        var canRecord = await deduplicator.TryRecordEventAsync("event-1");
        Assert.True(canRecord);
    }

    [Fact]
    public async Task AutoCleanup_ShouldRunPeriodically()
    {
        // Arrange
        var deduplicator = new MemoryCacheEventDeduplicator(new DeduplicationOptions
        {
            EventExpiration = TimeSpan.FromMilliseconds(50),
            MaxCachedEvents = 100,
            EnableAutoCleanup = true,
            CleanupInterval = TimeSpan.FromMilliseconds(100)
        });
        
        // Act
        await deduplicator.TryRecordEventAsync("event-1");
        await deduplicator.TryRecordEventAsync("event-2");
        
        // 等待自动清理
        await Task.Delay(200);
        
        var stats = await deduplicator.GetStatisticsAsync();
        
        // Assert
        // 过期的事件应该被清理
        Assert.Equal(0, stats.UniqueEvents);
    }

    [Fact]
    public async Task ConcurrentAccess_ShouldBeThreadSafe()
    {
        // Arrange
        var deduplicator = new MemoryCacheEventDeduplicator(new DeduplicationOptions
        {
            EventExpiration = TimeSpan.FromMinutes(5),
            MaxCachedEvents = 10000
        });
        
        var tasks = new List<Task<bool>>();
        var eventsPerThread = 100;
        var threadCount = 10;
        
        // Act
        // 多线程并发记录事件
        for (int i = 0; i < threadCount; i++)
        {
            var threadId = i;
            tasks.AddRange(Enumerable.Range(0, eventsPerThread).Select(j =>
                deduplicator.TryRecordEventAsync($"thread-{threadId}-event-{j}")));
        }
        
        var results = await Task.WhenAll(tasks);
        
        // Assert
        // 所有事件都应该成功记录（没有重复）
        Assert.All(results, r => Assert.True(r));
        
        var stats = await deduplicator.GetStatisticsAsync();
        Assert.Equal(threadCount * eventsPerThread, stats.UniqueEvents);
    }

    [Fact]
    public async Task MaxCachedEvents_ShouldLimitCacheSize()
    {
        // Arrange
        var maxEvents = 10;
        var deduplicator = new MemoryCacheEventDeduplicator(new DeduplicationOptions
        {
            EventExpiration = TimeSpan.FromMinutes(5),
            MaxCachedEvents = maxEvents,
            // 限制最大事件数
        });
        
        // Act
        // 添加超过最大限制的事件
        for (int i = 0; i < maxEvents * 2; i++)
        {
            await deduplicator.TryRecordEventAsync($"event-{i}");
        }
        
        // 手动触发清理
        await deduplicator.CleanupExpiredAsync();
        
        // Assert
        var stats = await deduplicator.GetStatisticsAsync();
        // 缓存大小应该被限制
        Assert.True(stats.CachedEvents <= maxEvents);
    }

    [Fact]
    public async Task CleanupExpiredAsync_ShouldReturnCleanedCount()
    {
        // Arrange
        var deduplicator = new MemoryCacheEventDeduplicator(new DeduplicationOptions
        {
            EventExpiration = TimeSpan.FromMilliseconds(50),
            MaxCachedEvents = 100
        });
        
        // 记录将要过期的事件
        await deduplicator.TryRecordEventAsync("event-1");
        await deduplicator.TryRecordEventAsync("event-2");
        
        // 等待过期
        await Task.Delay(100);
        
        // 记录新事件
        await deduplicator.TryRecordEventAsync("event-3");
        
        // Act
        var cleanedCount = await deduplicator.CleanupExpiredAsync();
        
        // Assert
        // 应该清理掉2个过期事件
        Assert.Equal(2, cleanedCount);
        
        var stats = await deduplicator.GetStatisticsAsync();
        Assert.Equal(1, stats.CachedEvents); // 只剩下event-3
    }

    [Fact]
    public async Task EmptyEventId_ShouldBeRejected()
    {
        // Arrange
        var deduplicator = new MemoryCacheEventDeduplicator(new DeduplicationOptions
        {
            EventExpiration = TimeSpan.FromMinutes(5),
            MaxCachedEvents = 100
        });
        
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await deduplicator.TryRecordEventAsync(null!));
        
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await deduplicator.TryRecordEventAsync(""));
        
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await deduplicator.TryRecordEventAsync("   "));
    }
}
