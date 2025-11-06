using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Aevatar.Agents.Abstractions;

namespace Aevatar.Agents.Core.Subscription;

/// <summary>
/// 基础订阅管理器实现
/// 提供统一的订阅管理、重试和健康检查机制
/// </summary>
public abstract class BaseSubscriptionManager : ISubscriptionManager
{
    protected readonly ILogger Logger;
    protected readonly ConcurrentDictionary<Guid, SubscriptionHandle> _subscriptions = new();
    
    protected BaseSubscriptionManager(ILogger? logger = null)
    {
        Logger = logger ?? NullLogger.Instance;
    }

    public async Task<ISubscriptionHandle> SubscribeWithRetryAsync(
        Guid parentId,
        Guid childId,
        Func<EventEnvelope, Task> eventHandler,
        IRetryPolicy? retryPolicy = null,
        CancellationToken cancellationToken = default)
    {
        retryPolicy ??= RetryPolicyFactory.CreateDefault();
        
        var subscriptionId = Guid.NewGuid();
        var handle = new SubscriptionHandle(subscriptionId, parentId, childId);
        
        Logger.LogDebug("Creating subscription {SubscriptionId}: Child {ChildId} -> Parent {ParentId}",
            subscriptionId, childId, parentId);
        
        Exception? lastException = null;
        
        for (int attempt = 1; attempt <= retryPolicy.MaxRetries + 1; attempt++)
        {
            try
            {
                // 调用具体实现创建订阅
                var streamSubscription = await CreateStreamSubscriptionAsync(
                    parentId, childId, eventHandler, cancellationToken);
                
                handle.StreamSubscription = streamSubscription;
                handle.IsHealthy = true;
                handle.LastActivityAt = DateTime.UtcNow;
                
                _subscriptions[subscriptionId] = handle;
                
                Logger.LogInformation(
                    "Successfully created subscription {SubscriptionId} after {Attempts} attempt(s)",
                    subscriptionId, attempt);
                
                return handle;
            }
            catch (Exception ex) when (attempt <= retryPolicy.MaxRetries && 
                                       retryPolicy.ShouldRetry(ex, attempt))
            {
                lastException = ex;
                handle.RetryCount = attempt;
                
                var delay = retryPolicy.GetDelay(attempt);
                
                Logger.LogWarning(ex,
                    "Failed to create subscription on attempt {Attempt}/{MaxRetries}. Retrying after {Delay}ms",
                    attempt, retryPolicy.MaxRetries + 1, delay.TotalMilliseconds);
                
                await Task.Delay(delay, cancellationToken);
            }
        }
        
        // 所有重试都失败了
        var errorMessage = $"Failed to create subscription after {retryPolicy.MaxRetries + 1} attempts";
        Logger.LogError(lastException, errorMessage);
        throw new InvalidOperationException(errorMessage, lastException);
    }

    public async Task<bool> IsSubscriptionHealthyAsync(ISubscriptionHandle subscription)
    {
        if (subscription == null)
        {
            return false;
        }
        
        // 检查是否在管理列表中
        if (!_subscriptions.ContainsKey(subscription.SubscriptionId))
        {
            return false;
        }
        
        // 调用具体实现检查健康状态
        var isHealthy = await CheckStreamHealthAsync(subscription);
        
        // 更新健康状态
        if (_subscriptions.TryGetValue(subscription.SubscriptionId, out var handle))
        {
            handle.IsHealthy = isHealthy;
            if (isHealthy)
            {
                handle.LastActivityAt = DateTime.UtcNow;
            }
        }
        
        return isHealthy;
    }

    public async Task ReconnectSubscriptionAsync(
        ISubscriptionHandle subscription,
        CancellationToken cancellationToken = default)
    {
        if (subscription == null)
        {
            throw new ArgumentNullException(nameof(subscription));
        }
        
        if (!_subscriptions.TryGetValue(subscription.SubscriptionId, out var handle))
        {
            throw new InvalidOperationException($"Subscription {subscription.SubscriptionId} not found");
        }
        
        Logger.LogInformation("Reconnecting subscription {SubscriptionId}", subscription.SubscriptionId);
        
        try
        {
            // 先尝试清理旧连接
            if (handle.StreamSubscription != null)
            {
                await handle.StreamSubscription.UnsubscribeAsync();
            }
            
            // 重新创建订阅
            // 注意：这里需要保存原始的eventHandler，但目前的设计中没有保存
            // 实际使用时可能需要在SubscriptionHandle中保存eventHandler
            await ReconnectStreamAsync(handle, cancellationToken);
            
            handle.IsHealthy = true;
            handle.LastActivityAt = DateTime.UtcNow;
            
            Logger.LogInformation("Successfully reconnected subscription {SubscriptionId}",
                subscription.SubscriptionId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to reconnect subscription {SubscriptionId}",
                subscription.SubscriptionId);
            
            handle.IsHealthy = false;
            throw;
        }
    }

    public async Task UnsubscribeAsync(
        ISubscriptionHandle subscription,
        CancellationToken cancellationToken = default)
    {
        if (subscription == null)
        {
            return;
        }
        
        if (_subscriptions.TryRemove(subscription.SubscriptionId, out var handle))
        {
            Logger.LogDebug("Unsubscribing {SubscriptionId}", subscription.SubscriptionId);
            
            try
            {
                if (handle.StreamSubscription != null)
                {
                    await handle.StreamSubscription.UnsubscribeAsync();
                }
                
                Logger.LogInformation("Successfully unsubscribed {SubscriptionId}",
                    subscription.SubscriptionId);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error during unsubscribe of {SubscriptionId}",
                    subscription.SubscriptionId);
            }
        }
    }

    public Task<IReadOnlyList<ISubscriptionHandle>> GetActiveSubscriptionsAsync()
    {
        var activeSubscriptions = _subscriptions.Values
            .Where(s => s.IsHealthy)
            .Cast<ISubscriptionHandle>()
            .ToList();
        
        return Task.FromResult<IReadOnlyList<ISubscriptionHandle>>(activeSubscriptions);
    }

    /// <summary>
    /// 创建流订阅（由具体运行时实现）
    /// </summary>
    protected abstract Task<IMessageStreamSubscription?> CreateStreamSubscriptionAsync(
        Guid parentId,
        Guid childId,
        Func<EventEnvelope, Task> eventHandler,
        CancellationToken cancellationToken);

    /// <summary>
    /// 检查流健康状态（由具体运行时实现）
    /// </summary>
    protected abstract Task<bool> CheckStreamHealthAsync(ISubscriptionHandle subscription);

    /// <summary>
    /// 重连流（由具体运行时实现）
    /// </summary>
    protected abstract Task ReconnectStreamAsync(
        SubscriptionHandle handle,
        CancellationToken cancellationToken);

    /// <summary>
    /// 内部订阅句柄实现
    /// </summary>
    protected class SubscriptionHandle : ISubscriptionHandle
    {
        public SubscriptionHandle(Guid subscriptionId, Guid parentId, Guid childId)
        {
            SubscriptionId = subscriptionId;
            ParentId = parentId;
            ChildId = childId;
            CreatedAt = DateTime.UtcNow;
            LastActivityAt = CreatedAt;
        }

        public Guid SubscriptionId { get; }
        public Guid ParentId { get; }
        public Guid ChildId { get; }
        public DateTime CreatedAt { get; }
        public DateTime LastActivityAt { get; set; }
        public bool IsHealthy { get; set; }
        public int RetryCount { get; set; }
        public IMessageStreamSubscription? StreamSubscription { get; set; }
    }
}

/// <summary>
/// 订阅管理器扩展方法
/// </summary>
public static class SubscriptionManagerExtensions
{
    /// <summary>
    /// 订阅并自动管理健康状态
    /// </summary>
    public static async Task<ISubscriptionHandle> SubscribeWithHealthCheckAsync(
        this ISubscriptionManager manager,
        Guid parentId,
        Guid childId,
        Func<EventEnvelope, Task> eventHandler,
        TimeSpan healthCheckInterval,
        CancellationToken cancellationToken = default)
    {
        var subscription = await manager.SubscribeWithRetryAsync(
            parentId, childId, eventHandler, cancellationToken: cancellationToken);
        
        // 启动健康检查任务
        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(healthCheckInterval, cancellationToken);
                
                if (!await manager.IsSubscriptionHealthyAsync(subscription))
                {
                    try
                    {
                        await manager.ReconnectSubscriptionAsync(subscription, cancellationToken);
                    }
                    catch
                    {
                        // 重连失败，下次再试
                    }
                }
            }
        }, cancellationToken);
        
        return subscription;
    }

    /// <summary>
    /// 批量取消订阅
    /// </summary>
    public static async Task UnsubscribeAllAsync(
        this ISubscriptionManager manager,
        CancellationToken cancellationToken = default)
    {
        var subscriptions = await manager.GetActiveSubscriptionsAsync();
        
        await Task.WhenAll(
            subscriptions.Select(s => manager.UnsubscribeAsync(s, cancellationToken))
        );
    }
}
