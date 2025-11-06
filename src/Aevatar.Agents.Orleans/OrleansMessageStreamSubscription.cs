using Aevatar.Agents.Abstractions;
using Orleans.Streams;

namespace Aevatar.Agents.Orleans;

/// <summary>
/// Orleans message stream subscription implementation
/// 包装Orleans的StreamSubscriptionHandle以支持取消订阅
/// </summary>
internal class OrleansMessageStreamSubscription : IMessageStreamSubscription
{
    private StreamSubscriptionHandle<byte[]> _handle;
    private readonly IAsyncObserver<byte[]> _observer;
    private readonly IAsyncStream<byte[]> _stream;
    private readonly Action _onDisposed;
    private bool _isActive;

    public Guid SubscriptionId { get; }
    public Guid StreamId { get; }
    public bool IsActive => _isActive;

    public OrleansMessageStreamSubscription(
        Guid subscriptionId,
        Guid streamId,
        StreamSubscriptionHandle<byte[]> handle,
        IAsyncObserver<byte[]> observer,
        IAsyncStream<byte[]> stream,
        Action onDisposed)
    {
        SubscriptionId = subscriptionId;
        StreamId = streamId;
        _handle = handle;
        _observer = observer;
        _stream = stream;
        _onDisposed = onDisposed;
        _isActive = true;
    }

    /// <summary>
    /// 取消订阅
    /// </summary>
    public async Task UnsubscribeAsync()
    {
        if (!_isActive)
        {
            return;
        }

        try
        {
            await _handle.UnsubscribeAsync();
            _isActive = false;
            _onDisposed?.Invoke();
        }
        catch (Exception ex)
        {
            // Log error but don't throw
            Console.WriteLine($"Error unsubscribing from stream {StreamId}: {ex.Message}");
        }
    }

    /// <summary>
    /// 恢复订阅
    /// </summary>
    public async Task ResumeAsync()
    {
        if (_isActive)
        {
            return;
        }

        try
        {
            // Orleans支持通过handle恢复订阅
            if (_handle != null)
            {
                // 使用保存的observer恢复订阅
                _handle = await _handle.ResumeAsync(_observer);
                _isActive = true;
            }
            else
            {
                // 如果handle已被释放，重新订阅
                _handle = await _stream.SubscribeAsync(_observer);
                _isActive = true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error resuming subscription to stream {StreamId}: {ex.Message}");
            // 如果恢复失败，尝试重新订阅
            try
            {
                _handle = await _stream.SubscribeAsync(_observer);
                _isActive = true;
            }
            catch
            {
                throw new InvalidOperationException(
                    $"Failed to resume subscription to stream {StreamId}. " +
                    "Please create a new subscription.", ex);
            }
        }
    }

    /// <summary>
    /// 异步释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await UnsubscribeAsync();
    }
}
