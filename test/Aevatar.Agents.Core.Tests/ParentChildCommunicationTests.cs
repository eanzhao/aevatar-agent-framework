using System;
using System.Threading.Tasks;
using Xunit;
using Shouldly;
using Aevatar.Agents.Core.Helpers;
using Aevatar.Agents.Core.Tests.Agents;
using Aevatar.Agents.Core.Tests.EventPublisher;
using Aevatar.Agents.Core.Tests.Fixtures;
using Aevatar.Agents.Abstractions;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.Core.Tests;

/// <summary>
/// Tests for parent-child communication using the actual framework features
/// </summary>
public class ParentChildCommunicationTests(CoreTestFixture fixture) : IClassFixture<CoreTestFixture>
{
    private readonly IServiceProvider _serviceProvider = fixture.ServiceProvider;
    private readonly TestEventPublisher _eventPublisher = fixture.EventPublisher;

    [Fact(DisplayName = "Child should send events UP to parent's stream")]
    public async Task Child_Should_Send_Events_Up_To_Parent()
    {
        // Arrange
        _eventPublisher.Clear();

        var childAgent = new RealChildAgent();
        var parentAgent = new RealParentAgent();

        AgentStateStoreInjector.InjectStateStore(childAgent, _serviceProvider);
        AgentStateStoreInjector.InjectStateStore(parentAgent, _serviceProvider);
        AgentEventPublisherInjector.InjectEventPublisher(childAgent, _eventPublisher);
        AgentEventPublisherInjector.InjectEventPublisher(parentAgent, _eventPublisher);

        // Act - Child publishes event with UP direction
        var testEvent = new TestEvent { EventId = "child-to-parent" };
        await childAgent.SendEventToParent(testEvent);

        // Assert - Event should be published with UP direction
        _eventPublisher.PublishedEvents.Count.ShouldBe(1);
        var publishedEvent = _eventPublisher.PublishedEvents[0];
        publishedEvent.Direction.ShouldBe(EventDirection.Up);
        publishedEvent.Event.ShouldBeOfType<TestEvent>();
        ((TestEvent)publishedEvent.Event).EventId.ShouldBe("child-to-parent");
    }

    [Fact(DisplayName = "Parent should broadcast events DOWN to children")]
    public async Task Parent_Should_Broadcast_Events_Down_To_Children()
    {
        // Arrange
        _eventPublisher.Clear();

        var parentAgent = new RealParentAgent();

        AgentStateStoreInjector.InjectStateStore(parentAgent, _serviceProvider);
        AgentEventPublisherInjector.InjectEventPublisher(parentAgent, _eventPublisher);

        // Act - Parent broadcasts event with DOWN direction
        var broadcastEvent = new TestCommand { CommandId = "parent-broadcast" };
        await parentAgent.BroadcastToChildren(broadcastEvent);

        // Assert - Event should be published with DOWN direction
        _eventPublisher.PublishedEvents.Count.ShouldBe(1);
        var publishedEvent = _eventPublisher.PublishedEvents[0];
        publishedEvent.Direction.ShouldBe(EventDirection.Down);
        publishedEvent.Event.ShouldBeOfType<TestCommand>();
        ((TestCommand)publishedEvent.Event).CommandId.ShouldBe("parent-broadcast");
    }

    [Fact(DisplayName = "Agent should broadcast events in BOTH directions")]
    public async Task Agent_Should_Broadcast_Events_Both_Directions()
    {
        // Arrange
        _eventPublisher.Clear();

        var agent = new RealChildAgent();

        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        AgentEventPublisherInjector.InjectEventPublisher(agent, _eventPublisher);

        // Act - Agent publishes event with BOTH direction
        var broadcastEvent = new TestEvent { EventId = "broadcast-both" };
        await agent.BroadcastToAll(broadcastEvent);

        // Assert - Event should be published with BOTH direction
        _eventPublisher.PublishedEvents.Count.ShouldBe(1);
        var publishedEvent = _eventPublisher.PublishedEvents[0];
        publishedEvent.Direction.ShouldBe(EventDirection.Both);
        publishedEvent.Event.ShouldBeOfType<TestEvent>();
        ((TestEvent)publishedEvent.Event).EventId.ShouldBe("broadcast-both");
    }

    [Fact(DisplayName = "Parent should receive and handle child's UP events")]
    public async Task Parent_Should_Handle_Child_Up_Events()
    {
        // Arrange
        _eventPublisher.Clear();

        var parentAgent = new RealParentAgent();

        AgentStateStoreInjector.InjectStateStore(parentAgent, _serviceProvider);
        AgentEventPublisherInjector.InjectEventPublisher(parentAgent, _eventPublisher);

        // Simulate child sending an event UP
        var childEvent = new TestEvent { EventId = "from-child" };
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(childEvent),
            PublisherId = Guid.NewGuid().ToString() // Child's ID
        };

        // Act - Parent handles the event
        await parentAgent.HandleEventAsync(envelope);

        // Assert - Parent should have processed the child's event
        parentAgent.ReceivedFromChildrenCount.ShouldBe(1);
        parentAgent.LastReceivedEventId.ShouldBe("from-child");
    }

    [Fact(DisplayName = "Child should receive and handle parent's DOWN events")]
    public async Task Child_Should_Handle_Parent_Down_Events()
    {
        // Arrange
        _eventPublisher.Clear();

        var childAgent = new RealChildAgent();

        AgentStateStoreInjector.InjectStateStore(childAgent, _serviceProvider);
        AgentEventPublisherInjector.InjectEventPublisher(childAgent, _eventPublisher);

        // Simulate parent broadcasting an event DOWN
        var parentCommand = new TestCommand { CommandId = "from-parent" };
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(parentCommand),
            PublisherId = Guid.NewGuid().ToString() // Parent's ID
        };

        // Act - Child handles the event
        await childAgent.HandleEventAsync(envelope);

        // Assert - Child should have processed the parent's command
        childAgent.ReceivedFromParentCount.ShouldBe(1);
        childAgent.LastReceivedCommandId.ShouldBe("from-parent");
    }

    [Fact(DisplayName = "Should propagate events between siblings")]
    public async Task Should_Propagate_Events_Between_Siblings()
    {
        // Arrange
        _eventPublisher.Clear();

        // Create parent and two child agents (siblings)
        var parentAgent = new RealParentAgent();
        var childAgent1 = new RealChildAgent();
        var childAgent2 = new RealChildAgent();

        // Inject dependencies
        AgentStateStoreInjector.InjectStateStore(parentAgent, _serviceProvider);
        AgentStateStoreInjector.InjectStateStore(childAgent1, _serviceProvider);
        AgentStateStoreInjector.InjectStateStore(childAgent2, _serviceProvider);
        AgentEventPublisherInjector.InjectEventPublisher(parentAgent, _eventPublisher);
        AgentEventPublisherInjector.InjectEventPublisher(childAgent1, _eventPublisher);
        AgentEventPublisherInjector.InjectEventPublisher(childAgent2, _eventPublisher);

        // Act - Child1 sends event UP to parent (which would broadcast to siblings)
        var siblingEvent = new TestEvent
        {
            EventId = "sibling-message",
            EventType = "sibling-to-sibling"
        };
        await childAgent1.SendEventToParent(siblingEvent);

        // Assert - Event should be published UP for parent to handle and potentially broadcast to other children
        _eventPublisher.PublishedEvents.Count.ShouldBe(1);
        var publishedEvent = _eventPublisher.PublishedEvents[0];
        publishedEvent.Direction.ShouldBe(EventDirection.Up);
        publishedEvent.Event.ShouldBeOfType<TestEvent>();
        ((TestEvent)publishedEvent.Event).EventId.ShouldBe("sibling-message");

        // Simulate parent broadcasting to children after receiving the UP event
        _eventPublisher.Clear();
        await parentAgent.BroadcastToChildren(siblingEvent);

        // Assert - Parent should broadcast DOWN to all children
        _eventPublisher.PublishedEvents.Count.ShouldBe(1);
        publishedEvent = _eventPublisher.PublishedEvents[0];
        publishedEvent.Direction.ShouldBe(EventDirection.Down);

        // Simulate child2 receiving the broadcast
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(siblingEvent),
            PublisherId = parentAgent.Id.ToString() // From parent
        };

        await childAgent2.HandleEventAsync(envelope);

        // Assert - Child2 should have received the event from sibling via parent
        childAgent2.ReceivedFromParentCount.ShouldBe(1);
        childAgent2.LastReceivedEventId.ShouldBe("sibling-message");
    }
}