using Shouldly;
using Aevatar.Agents.Core.Tests.Agents;
using Aevatar.Agents.Core.Tests.Fixtures;
using Aevatar.Agents.Abstractions.Attributes;
using System.Reflection;
using Google.Protobuf.WellKnownTypes;
using Aevatar.Agents.Core.Helpers;

namespace Aevatar.Agents.Core.Tests;

/// <summary>
/// Tests for event handler discovery, execution and filtering
/// </summary>
public class EventHandlerTests(CoreTestFixture fixture) : IClassFixture<CoreTestFixture>
{
    private readonly IServiceProvider _serviceProvider = fixture.ServiceProvider;

    #region EventHandler Discovery Tests

    [Fact(DisplayName = "Should discover event handlers automatically")]
    public void Should_Discover_Event_Handlers_Automatically()
    {
        // Arrange
        var agent = new BasicTestAgent();

        // Act
        var handlers = agent.GetEventHandlers();

        // Assert
        handlers.ShouldNotBeNull();
        handlers.Length.ShouldBeGreaterThan(0);

        // BasicTestAgent has 3 handlers: HandleTestEvent, HandleTestCommand, HandleAnyEvent
        handlers.Length.ShouldBe(3);
    }

    [Fact(DisplayName = "Should find methods with EventHandler attribute")]
    public void Should_Find_Methods_With_EventHandler_Attribute()
    {
        // Arrange
        var agent = new BasicTestAgent();

        // Act
        var handlers = agent.GetEventHandlers();

        // Assert
        var eventHandlerMethods = handlers
            .Where(h => h.GetCustomAttribute<EventHandlerAttribute>() != null)
            .ToArray();

        eventHandlerMethods.Length.ShouldBeGreaterThan(0);

        // Verify specific handlers
        eventHandlerMethods.Any(h => h.Name == "HandleTestEvent").ShouldBeTrue();
        eventHandlerMethods.Any(h => h.Name == "HandleTestCommand").ShouldBeTrue();
    }

    [Fact(DisplayName = "Should find handlers by naming convention")]
    public void Should_Find_Handlers_By_Naming_Convention()
    {
        // Arrange
        var agent = new ConventionBasedAgent();

        // Act
        var handlers = agent.GetEventHandlers();

        // Assert
        handlers.ShouldNotBeNull();

        // Should find HandleAsync and HandleEventAsync
        var handlerNames = handlers.Select(h => h.Name).ToArray();
        handlerNames.ShouldContain("HandleAsync");
        handlerNames.ShouldContain("HandleEventAsync");

        // Should NOT find ProcessEvent (doesn't follow convention)
        handlerNames.ShouldNotContain("ProcessEvent");
    }

    #endregion

    #region Event Handler Execution Tests

    [Fact(DisplayName = "Should execute event handlers synchronously")]
    public async Task Should_Execute_Event_Handlers_Synchronously()
    {
        // Arrange
        var agent = new BasicTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);

        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(new TestEvent
            {
                EventId = "test-1",
                EventType = "test",
                Payload = "test payload"
            }),
            PublisherId = "test-publisher"
        };

        // Act
        await agent.HandleEventAsync(envelope);

        // Assert
        agent.HandleEventCallCount.ShouldBe(1);
        agent.GetState().Counter.ShouldBe(1);
    }

    [Fact(DisplayName = "Should execute handlers by priority order")]
    public async Task Should_Execute_Handlers_By_Priority_Order()
    {
        // Arrange
        var agent = new PriorityTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);

        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(new TestEvent { EventId = "test-1" }),
            PublisherId = "test-publisher"
        };

        // Act
        await agent.HandleEventAsync(envelope);

        // Assert
        agent.ExecutionOrder.Count.ShouldBe(4);
        agent.ExecutionOrder[0].ShouldBe("High"); // Priority 1
        agent.ExecutionOrder[1].ShouldBe("Medium"); // Priority 5
        agent.ExecutionOrder[2].ShouldBe("Low"); // Priority 10
        agent.ExecutionOrder[3].ShouldBe("All"); // Priority 100 (AllEventHandler)
    }

    [Fact(DisplayName = "Should execute multiple handlers in sequence")]
    public async Task Should_Execute_Multiple_Handlers_In_Sequence()
    {
        // Arrange
        var agent = new BasicTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);

        // Send TestEvent
        var eventEnvelope = new EventEnvelope
        {
            Id = "event-1",
            Payload = Any.Pack(new TestEvent { EventId = "test-1" }),
            PublisherId = "publisher-1"
        };

        // Send TestCommand
        var commandEnvelope = new EventEnvelope
        {
            Id = "command-1",
            Payload = Any.Pack(new TestCommand
            {
                CommandId = "cmd-1",
                CommandType = "update"
            }),
            PublisherId = "publisher-1"
        };

        // Act
        await agent.HandleEventAsync(eventEnvelope);
        await agent.HandleEventAsync(commandEnvelope);

        // Assert
        agent.GetState().Counter.ShouldBe(11); // 1 from event + 10 from command
        agent.GetState().Items.Count.ShouldBe(2); // Both handled by AllEventHandler
    }

    #endregion

    #region All Event Handler Tests

    [Fact(DisplayName = "Should handle all events with AllEventHandler")]
    public async Task Should_Handle_All_Events_With_AllEventHandler()
    {
        // Arrange
        var agent = new BasicTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);

        // Different event types
        var envelopes = new[]
        {
            new EventEnvelope
            {
                Id = "1",
                Payload = Any.Pack(new TestEvent { EventId = "e1" }),
                PublisherId = "pub"
            },
            new EventEnvelope
            {
                Id = "2",
                Payload = Any.Pack(new TestCommand { CommandId = "c1" }),
                PublisherId = "pub"
            },
            new EventEnvelope
            {
                Id = "3",
                Payload = Any.Pack(new StringValue { Value = "s1" }),
                PublisherId = "pub"
            },
            new EventEnvelope
            {
                Id = "4",
                Payload = Any.Pack(new Int32Value { Value = 42 }),
                PublisherId = "pub"
            }
        };

        // Act
        foreach (var envelope in envelopes)
        {
            await agent.HandleEventAsync(envelope);
        }

        // Assert - AllEventHandler should handle all events
        agent.GetState().Items.Count.ShouldBe(4);
        agent.GetState().Items.ShouldContain("Event: 1");
        agent.GetState().Items.ShouldContain("Event: 2");
        agent.GetState().Items.ShouldContain("Event: 3");
        agent.GetState().Items.ShouldContain("Event: 4");
    }

    #endregion
}