using Shouldly;
using Aevatar.Agents.Core.Tests.Agents;
using Aevatar.Agents.Core.Tests.Fixtures;
using Aevatar.Agents.Core.Helpers;
using Google.Protobuf.WellKnownTypes;
using Aevatar.Agents.Core.Tests.EventPublisher;

namespace Aevatar.Agents.Core.Tests;

/// <summary>
/// Tests for event publishing mechanisms and directions
/// </summary>
public class EventPublishingTests(CoreTestFixture fixture) : IClassFixture<CoreTestFixture>
{
    private readonly IServiceProvider _serviceProvider = fixture.ServiceProvider;

    #region PublishAsync Tests

    [Fact(DisplayName = "Should publish events up to parent")]
    public async Task Should_Publish_Events_Up_To_Parent()
    {
        // Arrange
        var agent = new PublishingTestAgent();
        var publisher = new TestEventPublisher();
        AgentEventPublisherInjector.InjectEventPublisher(agent, publisher);
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);

        // Act
        var testEvent = new TestEvent { EventId = "test-up", EventType = "test" };
        await agent.PublishTestEventUpAsync(testEvent);

        // Assert
        publisher.PublishedEvents.Count.ShouldBe(1);
        var published = publisher.PublishedEvents[0];
        published.Direction.ShouldBe(EventDirection.Up);
        published.EventType.ShouldBe(nameof(TestEvent));
        var unpackedEvent = published.Event as TestEvent;
        unpackedEvent.ShouldNotBeNull();
        unpackedEvent.EventId.ShouldBe("test-up");
    }

    [Fact(DisplayName = "Should publish events down to children")]
    public async Task Should_Publish_Events_Down_To_Children()
    {
        // Arrange
        var agent = new PublishingTestAgent();
        var publisher = new TestEventPublisher();
        AgentEventPublisherInjector.InjectEventPublisher(agent, publisher);
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);

        // Act
        var testEvent = new TestEvent { EventId = "test-down", EventType = "broadcast" };
        await agent.PublishTestEventDownAsync(testEvent);

        // Assert
        publisher.PublishedEvents.Count.ShouldBe(1);
        var published = publisher.PublishedEvents[0];
        published.Direction.ShouldBe(EventDirection.Down);
        published.EventType.ShouldBe(nameof(TestEvent));
        var unpackedEvent = published.Event as TestEvent;
        unpackedEvent.ShouldNotBeNull();
        unpackedEvent.EventId.ShouldBe("test-down");
    }

    [Fact(DisplayName = "Should publish events in both directions")]
    public async Task Should_Publish_Events_In_Both_Directions()
    {
        // Arrange
        var agent = new PublishingTestAgent();
        var publisher = new TestEventPublisher();
        AgentEventPublisherInjector.InjectEventPublisher(agent, publisher);
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);

        // Act
        var testEvent = new TestEvent { EventId = "test-both", EventType = "broadcast-all" };
        await agent.PublishTestEventBothAsync(testEvent);

        // Assert
        publisher.PublishedEvents.Count.ShouldBe(1);
        var published = publisher.PublishedEvents[0];
        published.Direction.ShouldBe(EventDirection.Both);
        published.EventType.ShouldBe(nameof(TestEvent));
        var unpackedEvent = published.Event as TestEvent;
        unpackedEvent.ShouldNotBeNull();
        unpackedEvent.EventId.ShouldBe("test-both");
    }

    [Fact(DisplayName = "Should handle publish exceptions gracefully")]
    public async Task Should_Handle_Publish_Exceptions_Gracefully()
    {
        // Arrange
        var agent = new PublishingTestAgent();
        var publisher = new TestEventPublisher
        {
            ShouldThrowException = true,
            ExceptionMessage = "Publish failed"
        };
        AgentEventPublisherInjector.InjectEventPublisher(agent, publisher);
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);

        // Act & Assert
        var testEvent = new TestEvent { EventId = "test-error" };
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await agent.PublishTestEventDownAsync(testEvent));

        publisher.AttemptedPublishCount.ShouldBe(1);
        publisher.PublishedEvents.Count.ShouldBe(0); // Should not add to published list on failure
    }

    [Fact(DisplayName = "Should track multiple event publishes")]
    public async Task Should_Track_Multiple_Event_Publishes()
    {
        // Arrange
        var agent = new PublishingTestAgent();
        var publisher = new TestEventPublisher();
        AgentEventPublisherInjector.InjectEventPublisher(agent, publisher);
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);

        // Act - Publish multiple events
        await agent.PublishTestEventUpAsync(new TestEvent { EventId = "event-1" });
        await agent.PublishTestEventDownAsync(new TestEvent { EventId = "event-2" });
        await agent.PublishTestEventBothAsync(new TestEvent { EventId = "event-3" });

        // Assert
        publisher.PublishedEvents.Count.ShouldBe(3);
        publisher.PublishedEvents[0].Direction.ShouldBe(EventDirection.Up);
        publisher.PublishedEvents[1].Direction.ShouldBe(EventDirection.Down);
        publisher.PublishedEvents[2].Direction.ShouldBe(EventDirection.Both);
    }

    #endregion

    #region Self Event Handling Tests

    [Fact(DisplayName = "Should handle self-published events when enabled")]
    public async Task Should_Handle_Self_Published_Events_When_Enabled()
    {
        // Arrange
        var agent = new SelfEventTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);

        var envelope = new EventEnvelope
        {
            Id = "event-1",
            Payload = Any.Pack(new TestEvent { EventId = "test-1" }),
            PublisherId = agent.Id.ToString() // Self-published
        };

        // Act
        await agent.HandleEventAsync(envelope);

        // Assert
        agent.SelfEventHandledCount.ShouldBe(1);
        agent.GetState().Counter.ShouldBe(1);
    }

    [Fact(DisplayName = "Should ignore self-published events by default")]
    public async Task Should_Ignore_Self_Published_Events_By_Default()
    {
        // Arrange
        var agent = new SelfEventTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);

        var envelope = new EventEnvelope
        {
            Id = "command-1",
            Payload = Any.Pack(new TestCommand { CommandId = "cmd-1" }),
            PublisherId = agent.Id.ToString() // Self-published
        };

        // Act
        await agent.HandleEventAsync(envelope);

        // Assert
        agent.OtherEventHandledCount.ShouldBe(0); // Should be ignored
    }

    #endregion

    #region Event Metadata Tests

    [Fact(DisplayName = "Should add metadata to events")]
    public async Task Should_Add_Metadata_To_Events()
    {
        // Arrange
        var agent = new MetadataTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);

        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(new TestMetadata { Value = { ["key1"] = "value1", ["key2"] = "value2" } })
        };

        // Act
        await agent.HandleEventAsync(envelope);

        // Assert
        agent.ReceivedMetadata.ShouldNotBeNull();
        agent.ReceivedMetadata.ShouldContainKey("key1");
        agent.ReceivedMetadata["key1"].ShouldBe("value1");
        agent.ReceivedMetadata.ShouldContainKey("key2");
        agent.ReceivedMetadata["key2"].ShouldBe("value2");
    }

    [Fact(DisplayName = "Should propagate event metadata")]
    public async Task Should_Propagate_Event_Metadata()
    {
        // Arrange
        var agent = new BasicTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);

        var envelope = new EventEnvelope
        {
            Id = "test-id",
            CorrelationId = "correlation-123",
            Message = "Test message",
            PublisherId = "publisher-1",
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        var state = agent.GetState();
        state.Metadata["trace-id"] = "trace-123";

        // Act
        await agent.HandleEventAsync(envelope);

        // Assert
        envelope.Id.ShouldBe("test-id");
        envelope.CorrelationId.ShouldBe("correlation-123");
        envelope.Message.ShouldBe("Test message");
        envelope.PublisherId.ShouldBe("publisher-1");
        state.Metadata["trace-id"].ShouldBe("trace-123");
    }

    [Fact(DisplayName = "Should modify event metadata")]
    public async Task Should_Modify_Event_Metadata()
    {
        // Arrange
        var agent = new MetadataTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);

        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(new TestMetadata { Value = { ["original"] = "value" } })
        };

        // Act
        agent.ModifyMetadata = true;
        await agent.HandleEventAsync(envelope);

        // Assert
        agent.ReceivedMetadata["original"].ShouldBe("value");
        agent.ReceivedMetadata.ShouldContainKey("modified");
        agent.ReceivedMetadata["modified"].ShouldBe("true");
    }

    #endregion
}