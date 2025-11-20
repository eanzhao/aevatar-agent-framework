using Google.Protobuf;

namespace Aevatar.Agents.Abstractions;

/// <summary>
/// 事件发布器接口
/// Agent 通过此接口发布事件，由 Actor 层实现具体的路由逻辑
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// 发布事件
    /// </summary>
    /// <param name="evt">事件消息</param>
    /// <param name="direction">传播方向（默认 Down）</param>
    /// <param name="ct">取消令牌</param>
    /// <typeparam name="TEvent">事件类型</typeparam>
    /// <returns>事件ID</returns>
    Task<string> PublishEventAsync<TEvent>(
        TEvent evt,
        EventDirection direction = EventDirection.Down,
        CancellationToken ct = default)
        where TEvent : IMessage;
}

/// <summary>
/// Not useful but should be injected to agent once its initialization.
/// </summary>
public sealed class NullEventPublisher : IEventPublisher
{
    public static NullEventPublisher Instance { get; } = new NullEventPublisher();

    public Task<string> PublishEventAsync<TEvent>(TEvent evt, EventDirection direction = EventDirection.Down,
        CancellationToken ct = default) where TEvent : IMessage
    {
        return Task.FromResult(string.Empty);
    }
}