using Aevatar.Agents.Abstractions;

namespace Aevatar.Agents.Local;

/// <summary>
/// Local message stream subscription implementation
/// 管理本地消息流的订阅生命周期
/// </summary>
internal class LocalMessageStreamSubscription : IMessageStreamSubscription
{
    private readonly Func<EventEnvelope, Task> _handler;
    private readonly Action _onDisposed;
    private bool _isActive;

    public Guid SubscriptionId { get; }
    public Guid StreamId { get; }
    public bool IsActive => _isActive;

    public LocalMessageStreamSubscription(
        Guid subscriptionId,
        Guid streamId,
        Func<EventEnvelope, Task> handler,
        Action onDisposed)
    {
        SubscriptionId = subscriptionId;
        StreamId = streamId;
        _handler = handler;
        _onDisposed = onDisposed;
        _isActive = true;
    }

    /// <summary>
    /// 处理接收到的消息
    /// </summary>
    public async Task HandleMessageAsync(EventEnvelope envelope)
    {
        if (!_isActive)
        {
            return;
        }

        await _handler(envelope);
    }

    /// <summary>
    /// 取消订阅
    /// </summary>
    public Task UnsubscribeAsync()
    {
        if (!_isActive)
        {
            return Task.CompletedTask;
        }

        _isActive = false;
        _onDisposed?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 恢复订阅
    /// </summary>
    public Task ResumeAsync()
    {
        if (_isActive)
        {
            return Task.CompletedTask;
        }

        // Local stream基于内存Channel
        // 恢复订阅只需要重新激活处理标志
        _isActive = true;
        
        // 注意：如果Channel已关闭，无法恢复
        // 调用方应该处理这种情况并创建新订阅
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// 异步释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await UnsubscribeAsync();
    }
}
