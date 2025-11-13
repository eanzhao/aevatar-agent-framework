using System;
using System.Threading.Tasks;

namespace Aevatar.Agents.Abstractions;

/// <summary>
/// 事件去重器接口
/// 提供基于时间窗口的事件去重机制
/// </summary>
public interface IEventDeduplicator
{
    /// <summary>
    /// 尝试记录事件
    /// </summary>
    /// <param name="eventId">事件ID</param>
    /// <returns>如果是新事件返回true，重复事件返回false</returns>
    Task<bool> TryRecordEventAsync(string eventId);

    /// <summary>
    /// 批量记录事件
    /// </summary>
    /// <param name="eventIds">事件ID列表</param>
    /// <returns>新事件ID列表</returns>
    Task<IReadOnlyList<string>> TryRecordEventsAsync(IReadOnlyList<string> eventIds);

    /// <summary>
    /// 检查事件是否已处理
    /// </summary>
    /// <param name="eventId">事件ID</param>
    /// <returns>是否已处理</returns>
    Task<bool> IsProcessedAsync(string eventId);

    /// <summary>
    /// 清理过期的事件记录
    /// </summary>
    /// <returns>清理的记录数</returns>
    Task<int> CleanupExpiredAsync();

    /// <summary>
    /// 获取统计信息
    /// </summary>
    /// <returns>去重统计</returns>
    Task<DeduplicationStatistics> GetStatisticsAsync();

    /// <summary>
    /// 重置去重器
    /// </summary>
    Task ResetAsync();
}

/// <summary>
/// 去重统计信息
/// </summary>
public class DeduplicationStatistics
{
    /// <summary>
    /// 总事件数
    /// </summary>
    public long TotalEvents { get; set; }

    /// <summary>
    /// 重复事件数
    /// </summary>
    public long DuplicateEvents { get; set; }

    /// <summary>
    /// 唯一事件数
    /// </summary>
    public long UniqueEvents { get; set; }

    /// <summary>
    /// 当前缓存的事件数
    /// </summary>
    public long CachedEvents { get; set; }

    /// <summary>
    /// 内存使用量（字节）
    /// </summary>
    public long MemoryUsageBytes { get; set; }

    /// <summary>
    /// 最后清理时间
    /// </summary>
    public DateTime? LastCleanupTime { get; set; }

    /// <summary>
    /// 重复率
    /// </summary>
    public double DuplicateRate => TotalEvents > 0 ? (double)DuplicateEvents / TotalEvents : 0;
}

/// <summary>
/// 去重配置
/// </summary>
public class DeduplicationOptions
{
    /// <summary>
    /// 事件过期时间（默认5分钟）
    /// </summary>
    public TimeSpan EventExpiration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 最大缓存事件数（默认100000）
    /// </summary>
    public int MaxCachedEvents { get; set; } = 100_000;

    /// <summary>
    /// 自动清理间隔（默认1分钟）
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 是否启用自动清理（默认true）
    /// </summary>
    public bool EnableAutoCleanup { get; set; } = true;

    /// <summary>
    /// 清理批大小（默认1000）
    /// </summary>
    public int CleanupBatchSize { get; set; } = 1000;
}

