using Shouldly;
using Aevatar.Agents.Core.Helpers;
using Aevatar.Agents.Core.Tests.Agents;
using Aevatar.Agents.Core.Tests.Fixtures;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core.Tests.EventPublisher;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Core.Tests;

/// <summary>
/// Tests for exception handling in event handlers
/// </summary>
public class ExceptionHandlerTests(CoreTestFixture fixture) : IClassFixture<CoreTestFixture>
{
    private readonly IServiceProvider _serviceProvider = fixture.ServiceProvider;
    private readonly TestEventPublisher _eventPublisher = fixture.EventPublisher;

    #region Basic Exception Handling

    [Fact(DisplayName = "Should catch handler exceptions and not propagate")]
    public async Task Should_Catch_Handler_Exceptions_And_Not_Propagate()
    {
        // Arrange
        _eventPublisher.Clear();
        var agent = new ExceptionTestAgent
        {
            ThrowInHandler = true
        };
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        AgentEventPublisherInjector.InjectEventPublisher(agent, _eventPublisher);

        var envelope = new EventEnvelope
        {
            Id = "event-1",
            Payload = Any.Pack(new TestEvent { EventId = "test-1" }),
            PublisherId = "publisher-1"
        };

        // Act - Should not throw, exceptions are caught internally
        var exception = await Record.ExceptionAsync(async () =>
            await agent.HandleEventAsync(envelope));

        // Assert
        exception.ShouldBeNull(); // No exception should propagate
        agent.HandlerExceptionCount.ShouldBe(1);
        
        // Verify exception event was published
        _eventPublisher.PublishedEvents.Count.ShouldBe(1);
        _eventPublisher.PublishedEvents[0].Event.ShouldBeOfType<EventHandlerExceptionEvent>();
    }

    #endregion

    #region Exception Event Publishing

    [Fact(DisplayName = "Should publish exception event when handler throws")]
    public async Task Should_Publish_Exception_Event_When_Handler_Throws()
    {
        // Arrange
        _eventPublisher.Clear();
        var agent = new ExceptionTestAgent { ThrowInHandler = true };
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        AgentEventPublisherInjector.InjectEventPublisher(agent, _eventPublisher);
        
        var envelope = new EventEnvelope
        {
            Id = "test-1",
            Payload = Any.Pack(new TestEvent { EventId = "error-test" }),
            PublisherId = "publisher"
        };
        
        // Act
        await agent.HandleEventAsync(envelope);
        
        // Assert
        _eventPublisher.PublishedEvents.Count.ShouldBe(1);
        var publishedEvent = _eventPublisher.PublishedEvents[0];
        
        publishedEvent.Direction.ShouldBe(EventDirection.Up);
        publishedEvent.Event.ShouldBeOfType<EventHandlerExceptionEvent>();
        
        var exceptionEvent = (EventHandlerExceptionEvent)publishedEvent.Event;
        exceptionEvent.ExceptionMessage.ShouldContain("InvalidOperationException: Handler failed");
        exceptionEvent.HandlerName.ShouldBe("ThrowingHandler");
        exceptionEvent.EventId.ShouldBe(envelope.Id);
        
        // The second handler should still be executed
        agent.SuccessfulHandlerCount.ShouldBe(1);
        agent.GetState().Counter.ShouldBe(1);
    }

    [Fact(DisplayName = "Should include stack trace in exception event")]
    public async Task Should_Include_Stack_Trace_In_Exception_Event()
    {
        // Arrange
        _eventPublisher.Clear();
        var agent = new ExceptionTestAgent { ThrowInHandler = true };
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        AgentEventPublisherInjector.InjectEventPublisher(agent, _eventPublisher);
        
        var envelope = new EventEnvelope
        {
            Id = "test-2",
            Payload = Any.Pack(new TestEvent { EventId = "test" }),
            PublisherId = "publisher"
        };
        
        // Act
        await agent.HandleEventAsync(envelope);
        
        // Assert
        _eventPublisher.PublishedEvents.Count.ShouldBe(1);
        var exceptionEvent = (EventHandlerExceptionEvent)_eventPublisher.PublishedEvents[0].Event;
        
        exceptionEvent.StackTrace.ShouldNotBeNullOrEmpty();
        exceptionEvent.StackTrace.ShouldContain("ThrowingHandler"); // Should contain method name
    }

    #endregion

    #region Handler Execution Continuation

    [Fact(DisplayName = "Handler exception should not affect other handlers")]
    public async Task Handler_Exception_Should_Not_Affect_Other_Handlers()
    {
        // Arrange
        _eventPublisher.Clear();
        var agent = new ExceptionTestAgent
        {
            ThrowInHandler = true
        };
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        AgentEventPublisherInjector.InjectEventPublisher(agent, _eventPublisher);

        // Send event that will trigger both handlers
        var envelope = new EventEnvelope
        {
            Id = "event-1",
            Payload = Any.Pack(new TestEvent { EventId = "test-1" }),
            PublisherId = "publisher-1"
        };

        // Act
        await agent.HandleEventAsync(envelope);

        // Assert
        agent.HandlerExceptionCount.ShouldBe(1); // First handler threw exception
        agent.SuccessfulHandlerCount.ShouldBe(1); // Second handler executed successfully
        agent.GetState().Counter.ShouldBe(1);    // State was modified by successful handler
        
        // Verify exception event was published
        _eventPublisher.PublishedEvents.Count.ShouldBe(1);
        _eventPublisher.PublishedEvents[0].Event.ShouldBeOfType<EventHandlerExceptionEvent>();
    }

    [Fact(DisplayName = "Should continue processing events after handler exception")]
    public async Task Should_Continue_Processing_Events_After_Handler_Exception()
    {
        // Arrange
        _eventPublisher.Clear();
        var agent = new ExceptionTestAgent
        {
            ThrowInHandler = true
        };
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        AgentEventPublisherInjector.InjectEventPublisher(agent, _eventPublisher);

        // Send multiple events
        var envelope1 = new EventEnvelope
        {
            Id = "event-1",
            Payload = Any.Pack(new TestEvent { EventId = "test-1" }),
            PublisherId = "publisher-1"
        };

        var envelope2 = new EventEnvelope
        {
            Id = "event-2",
            Payload = Any.Pack(new TestEvent { EventId = "test-2" }),
            PublisherId = "publisher-1"
        };

        // Act
        await agent.HandleEventAsync(envelope1);
        await agent.HandleEventAsync(envelope2);

        // Assert
        agent.SuccessfulHandlerCount.ShouldBe(2); // Both successful handlers executed
        agent.HandlerExceptionCount.ShouldBe(2);  // Both throwing handlers failed
        agent.GetState().Counter.ShouldBe(2);     // State incremented twice
        
        // Verify exception events were published for both
        _eventPublisher.PublishedEvents.Count.ShouldBe(2);
        _eventPublisher.PublishedEvents.All(e => e.Event is EventHandlerExceptionEvent).ShouldBeTrue();
    }

    #endregion

    #region AllEventHandler Exception Handling

    [Fact(DisplayName = "Should handle exceptions in AllEventHandler")]
    public async Task Should_Handle_Exceptions_In_AllEventHandler()
    {
        // Arrange
        _eventPublisher.Clear();
        var agent = new AllEventHandlerExceptionAgent { ThrowInHandler = true };
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        AgentEventPublisherInjector.InjectEventPublisher(agent, _eventPublisher);
        
        var envelope = new EventEnvelope
        {
            Id = "test-all",
            Payload = Any.Pack(new TestEvent { EventId = "all-test" }),
            PublisherId = "publisher"
        };
        
        // Act
        await agent.HandleEventAsync(envelope);
        
        // Assert
        _eventPublisher.PublishedEvents.Count.ShouldBe(1);
        var exceptionEvent = (EventHandlerExceptionEvent)_eventPublisher.PublishedEvents[0].Event;
        
        exceptionEvent.HandlerName.ShouldBe("HandleAllWithException");
        exceptionEvent.ExceptionMessage.ShouldContain("InvalidOperationException: AllEventHandler failed");
    }

    #endregion

    #region Exception Types

    [Fact(DisplayName = "Should handle different exception types")]
    public async Task Should_Handle_Different_Exception_Types()
    {
        // Arrange
        _eventPublisher.Clear();
        var agent = new MultiExceptionAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        AgentEventPublisherInjector.InjectEventPublisher(agent, _eventPublisher);
        
        var envelope = new EventEnvelope
        {
            Id = "multi-exception",
            Payload = Any.Pack(new TestEvent { EventId = "test" }),
            PublisherId = "publisher"
        };
        
        // Act
        await agent.HandleEventAsync(envelope);
        
        // Assert
        _eventPublisher.PublishedEvents.Count.ShouldBe(3); // Three different handlers threw exceptions
        
        var exceptionEvents = _eventPublisher.PublishedEvents
            .Select(e => (EventHandlerExceptionEvent)e.Event)
            .ToList();
        
        // Verify different exception types were captured
        exceptionEvents.ShouldContain(e => e.ExceptionMessage.Contains("Invalid operation"));
        exceptionEvents.ShouldContain(e => e.ExceptionMessage.Contains("Invalid argument"));
        exceptionEvents.ShouldContain(e => e.ExceptionMessage.Contains("Not implemented"));
        
        // Verify the successful handler still executed
        agent.GetState().Counter.ShouldBe(1);
    }

    #endregion

    #region Exception Event Details

    [Fact(DisplayName = "Exception event should contain all required details")]
    public async Task Exception_Event_Should_Contain_All_Required_Details()
    {
        // Arrange
        _eventPublisher.Clear();
        var agent = new DetailedExceptionAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        AgentEventPublisherInjector.InjectEventPublisher(agent, _eventPublisher);
        
        var envelope = new EventEnvelope
        {
            Id = "detailed-test",
            CorrelationId = "correlation-123",
            Payload = Any.Pack(new TestEvent { EventId = "test-detail" }),
            PublisherId = "test-publisher"
        };
        
        // Act
        await agent.HandleEventAsync(envelope);
        
        // Assert
        _eventPublisher.PublishedEvents.Count.ShouldBe(1);
        var exceptionEvent = (EventHandlerExceptionEvent)_eventPublisher.PublishedEvents[0].Event;
        
        // Verify all details are captured including the complete exception chain
        exceptionEvent.EventId.ShouldBe("detailed-test");
        exceptionEvent.HandlerName.ShouldBe("ThrowDetailedException");
        
        // The exception message should now contain the complete exception chain
        exceptionEvent.ExceptionMessage.ShouldContain("InvalidOperationException: Detailed exception with inner");
        exceptionEvent.ExceptionMessage.ShouldContain("---> ArgumentException: Inner exception message");
        
        exceptionEvent.StackTrace.ShouldNotBeNullOrEmpty();
        exceptionEvent.StackTrace.ShouldContain("ThrowDetailedException");
    }

    #endregion
}

/// <summary>
/// Test agent for AllEventHandler exception testing
/// </summary>
public class AllEventHandlerExceptionAgent : GAgentBase<TestAgentState>
{
    public bool ThrowInHandler { get; set; }
    
    public override string GetDescription() => "AllEventHandlerExceptionAgent";
    
    [AllEventHandler]
    public async Task HandleAllWithException(EventEnvelope envelope)
    {
        if (ThrowInHandler)
        {
            throw new InvalidOperationException("AllEventHandler failed");
        }
        await Task.CompletedTask;
    }
}

/// <summary>
/// Test agent with multiple exception types
/// </summary>
public class MultiExceptionAgent : GAgentBase<TestAgentState>
{
    public override string GetDescription() => "MultiExceptionAgent";
    
    [EventHandler(Priority = 1)]
    public async Task ThrowInvalidOperation(TestEvent evt)
    {
        throw new InvalidOperationException("Invalid operation in handler");
    }
    
    [EventHandler(Priority = 2)]
    public async Task ThrowArgument(TestEvent evt)
    {
        throw new ArgumentException("Invalid argument in handler");
    }
    
    [EventHandler(Priority = 3)]
    public async Task ThrowNotImplemented(TestEvent evt)
    {
        throw new NotImplementedException("Not implemented in handler");
    }
    
    [EventHandler(Priority = 4)]
    public async Task SuccessfulHandler(TestEvent evt)
    {
        State.Counter++;
        await Task.CompletedTask;
    }
}

/// <summary>
/// Test agent for detailed exception information
/// </summary>
public class DetailedExceptionAgent : GAgentBase<TestAgentState>
{
    public override string GetDescription() => "DetailedExceptionAgent";
    
    [EventHandler]
    public async Task ThrowDetailedException(TestEvent evt)
    {
        try
        {
            throw new ArgumentException("Inner exception message");
        }
        catch (Exception inner)
        {
            throw new InvalidOperationException("Detailed exception with inner", inner);
        }
    }
}