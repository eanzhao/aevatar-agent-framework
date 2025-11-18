
using Aevatar.Agents.Abstractions;
using Google.Protobuf;

namespace Aevatar.Agents.Core.Tests.EventPublisher;

/// <summary>
/// Simple test implementation of IEventPublisher for testing purposes
/// Tracks all published events and their directions
/// </summary>
public class TestEventPublisher : IEventPublisher
{
    public class PublishedEventInfo
    {
        public IMessage Event { get; set; }
        public EventDirection Direction { get; set; }
        public string EventType { get; set; }
        public string EventId { get; set; }
        public DateTime PublishedAt { get; set; }
    }

    public List<PublishedEventInfo> PublishedEvents { get; } = new();
    public int AttemptedPublishCount { get; private set; }
    public bool ShouldThrowException { get; set; }
    public string ExceptionMessage { get; set; } = "Test exception";

    public Task<string> PublishEventAsync<TEvent>(TEvent evt, EventDirection direction, CancellationToken ct = default)
        where TEvent : IMessage
    {
        AttemptedPublishCount++;

        if (ShouldThrowException)
        {
            throw new InvalidOperationException(ExceptionMessage);
        }

        var eventId = Guid.NewGuid().ToString();
        PublishedEvents.Add(new PublishedEventInfo
        {
            Event = evt,
            Direction = direction,
            EventType = typeof(TEvent).Name,
            EventId = eventId,
            PublishedAt = DateTime.UtcNow
        });

        return Task.FromResult(eventId);
    }

    public void Clear()
    {
        PublishedEvents.Clear();
        AttemptedPublishCount = 0;
    }
}