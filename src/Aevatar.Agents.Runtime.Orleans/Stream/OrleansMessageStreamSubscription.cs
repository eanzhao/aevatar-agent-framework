using Aevatar.Agents.Abstractions;
using Orleans.Streams;

namespace Aevatar.Agents.Runtime.Orleans.Stream;

/// <summary>
/// Orleans stream subscription implementation.
/// Wraps Orleans StreamSubscriptionHandle to provide IMessageStreamSubscription interface.
/// </summary>
public class OrleansMessageStreamSubscription : IMessageStreamSubscription
{
    private readonly StreamSubscriptionHandle<byte[]> _streamSubscriptionHandle;
    private readonly Action _onDisposed;
    private bool _isActive;

    public Guid SubscriptionId { get; }
    public string StreamId { get; }
    public bool IsActive => _isActive && _streamSubscriptionHandle != null;

    public OrleansMessageStreamSubscription(
        Guid subscriptionId,
        string streamId,
        StreamSubscriptionHandle<byte[]> streamSubscriptionHandle,
        Action onDisposed)
    {
        SubscriptionId = subscriptionId;
        StreamId = streamId;
        _streamSubscriptionHandle = streamSubscriptionHandle ?? throw new ArgumentNullException(nameof(streamSubscriptionHandle));
        _onDisposed = onDisposed;
        _isActive = true;
    }

    /// <inheritdoc />
    public async Task UnsubscribeAsync()
    {
        if (!_isActive)
        {
            return;
        }

        try
        {
            await _streamSubscriptionHandle.UnsubscribeAsync();
            _isActive = false;
            _onDisposed?.Invoke();
        }
        catch (Exception)
        {
            // If unsubscribe fails, mark as inactive anyway
            _isActive = false;
            _onDisposed?.Invoke();
        }
    }

    /// <inheritdoc />
    public async Task ResumeAsync()
    {
        if (_isActive)
        {
            return;
        }

        try
        {
            // Orleans Stream handles resumption automatically through StreamSubscriptionHandle
            // If the handle is still valid, we can mark as active
            // Note: Orleans doesn't have explicit "resume" - it handles reconnection automatically
            _isActive = true;
            await Task.CompletedTask;
        }
        catch (Exception)
        {
            // Resume failed - subscription might be invalid
            _isActive = false;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await UnsubscribeAsync();
    }
}
