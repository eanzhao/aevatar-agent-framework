using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Aevatar.Agents.Core.Tests;

public class GAgentBaseTests
{
    private readonly Mock<ILogger<TestAgent>> _mockLogger = new();
    private readonly Mock<IEventPublisher> _mockEventPublisher = new();
    
    [Fact(DisplayName = "GAgentBase should initialize with new Guid when not provided")]
    public void GAgentBase_ShouldInitializeWithNewGuidWhenNotProvided()
    {
        // Arrange & Act
        var agent1 = new TestAgentForBase();
        var agent2 = new TestAgentForBase();
        
        // Assert
        agent1.Id.Should().NotBe(Guid.Empty);
        agent2.Id.Should().NotBe(Guid.Empty);
        agent1.Id.Should().NotBe(agent2.Id);
    }
    
    [Fact(DisplayName = "GAgentBase should use provided Guid")]
    public void GAgentBase_ShouldUseProvidedGuid()
    {
        // Arrange
        var expectedId = Guid.NewGuid();
        
        // Act
        var agent = new TestAgentForBase(expectedId);
        
        // Assert
        agent.Id.Should().Be(expectedId);
    }
    
    [Fact(DisplayName = "GAgentBase should initialize state with new instance")]
    public void GAgentBase_ShouldInitializeStateWithNewInstance()
    {
        // Arrange & Act
        var agent = new TestAgentForBase();
        var state = agent.GetState();
        
        // Assert
        state.Should().NotBeNull();
        state.Name.Should().BeEmpty();
        state.Counter.Should().Be(0);
        state.IsActive.Should().BeFalse();
        state.Items.Should().BeEmpty();
        state.Attributes.Should().BeEmpty();
    }
    
    [Fact(DisplayName = "SetEventPublisher should set the event publisher")]
    public void SetEventPublisher_ShouldSetTheEventPublisher()
    {
        // Arrange
        var agent = new TestAgentForBase();
        
        // Act
        agent.SetEventPublisher(_mockEventPublisher.Object);
        
        // Assert
        agent.EventPublisher.Should().Be(_mockEventPublisher.Object);
    }
    
    [Fact(DisplayName = "PublishAsync should throw when EventPublisher is not set")]
    public async Task PublishAsync_ShouldThrowWhenEventPublisherIsNotSet()
    {
        // Arrange
        var agent = new TestAgentForBase();
        var testEvent = new TestEvent { EventId = "test-123" };
        
        // Act
        var act = () => agent.PublishEventAsync(testEvent);
        
        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*EventPublisher is not set*");
    }
    
    [Fact(DisplayName = "PublishAsync should delegate to EventPublisher when set")]
    public async Task PublishAsync_ShouldDelegateToEventPublisherWhenSet()
    {
        // Arrange
        var agent = new TestAgentForBase();
        agent.SetEventPublisher(_mockEventPublisher.Object);
        var testEvent = new TestEvent { EventId = "test-123" };
        var expectedResponse = "published";
        
        _mockEventPublisher
            .Setup(p => p.PublishEventAsync(testEvent, EventDirection.Down, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);
        
        // Act
        var result = await agent.PublishEventAsync(testEvent);
        
        // Assert
        result.Should().Be(expectedResponse);
        _mockEventPublisher.Verify(
            p => p.PublishEventAsync(testEvent, EventDirection.Down, It.IsAny<CancellationToken>()), 
            Times.Once);
    }
    
    [Fact(DisplayName = "GetEventHandlers should discover event handler methods")]
    public void GetEventHandlers_ShouldDiscoverEventHandlerMethods()
    {
        // Arrange
        var agent = new TestAgentWithMultipleHandlers();
        
        // Act
        var handlers = agent.GetEventHandlers();
        
        // Assert
        handlers.Should().NotBeNull();
        handlers.Length.Should().BeGreaterThan(0);
        
        // Check specific handlers are found
        var handlerNames = handlers.Select(h => h.Name).ToList();
        handlerNames.Should().Contain("HandleTestEvent");
        handlerNames.Should().Contain("HandleTestAddItem");
        handlerNames.Should().Contain("HandleAllEvents");
        handlerNames.Should().Contain("HandleEventAsync"); // Default handler pattern
    }
    
    [Fact(DisplayName = "GetEventHandlers should order handlers by priority")]
    public void GetEventHandlers_ShouldOrderHandlersByPriority()
    {
        // Arrange
        var agent = new TestAgentWithPriorityHandlers();
        
        // Act
        var handlers = agent.GetEventHandlers();
        
        // Assert
        handlers.Should().NotBeNull();
        handlers.Length.Should().BeGreaterThanOrEqualTo(3);
        
        // High priority (-10) should come first
        handlers[0].Name.Should().Be("HighPriorityHandler");
        // Normal priority (0) should be in the middle
        handlers[1].Name.Should().Be("NormalPriorityHandler");
        // Low priority (100) should come later
        handlers[2].Name.Should().Be("LowPriorityHandler");
    }
    
    [Fact(DisplayName = "GetEventHandlers should cache results for performance")]
    public void GetEventHandlers_ShouldCacheResultsForPerformance()
    {
        // Arrange
        var agent1 = new TestAgentWithMultipleHandlers();
        var agent2 = new TestAgentWithMultipleHandlers();
        
        // Act
        var handlers1First = agent1.GetEventHandlers();
        var handlers1Second = agent1.GetEventHandlers();
        var handlers2 = agent2.GetEventHandlers();
        
        // Assert
        handlers1First.Should().BeSameAs(handlers1Second); // Cached for same type
        handlers1First.Should().BeSameAs(handlers2); // Cached across instances of same type
    }
    
    [Fact(DisplayName = "HandleEventAsync should invoke matching event handlers")]
    public async Task HandleEventAsync_ShouldInvokeMatchingEventHandlers()
    {
        // Arrange
        var agent = new TestAgentWithCounters();
        agent.SetEventPublisher(_mockEventPublisher.Object);
        
        var testEvent = new TestEvent { EventId = "test-123", EventData = "TestData" };
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            PublisherId = "other-agent",
            Payload = Any.Pack(testEvent)
        };
        
        // Act
        await agent.HandleEventAsync(envelope);
        
        // Assert
        agent.TestEventCount.Should().Be(1);
        agent.AllEventCount.Should().Be(1);
        agent.GetState().Name.Should().Be("TestData");
    }
    
    [Fact(DisplayName = "HandleEventAsync should respect AllowSelfHandling flag")]
    public async Task HandleEventAsync_ShouldRespectAllowSelfHandlingFlag()
    {
        // Arrange
        var agent = new TestAgentWithSelfHandling();
        agent.SetEventPublisher(_mockEventPublisher.Object);
        
        var testEvent = new TestEvent { EventId = "test-123" };
        var envelopeFromSelf = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            PublisherId = agent.Id.ToString(),
            Payload = Any.Pack(testEvent)
        };
        
        var envelopeFromOther = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            PublisherId = "other-agent",
            Payload = Any.Pack(testEvent)
        };
        
        // Act
        await agent.HandleEventAsync(envelopeFromSelf);
        await agent.HandleEventAsync(envelopeFromOther);
        
        // Assert
        agent.SelfHandlingCount.Should().Be(2); // Invoked for both events (AllowSelfHandling=true)
        agent.NoSelfHandlingCount.Should().Be(1); // Only invoked for other agent's event (AllowSelfHandling=false)
    }
    
    [Fact(DisplayName = "HandleEventAsync should handle exceptions gracefully")]
    public async Task HandleEventAsync_ShouldHandleExceptionsGracefully()
    {
        // Arrange
        var agent = new TestAgentWithFaultyHandler();
        agent.SetEventPublisher(_mockEventPublisher.Object);
        
        // Setup the mock to handle any IMessage type
        _mockEventPublisher
            .Setup(p => p.PublishEventAsync(
                It.IsAny<Google.Protobuf.IMessage>(), 
                It.IsAny<EventDirection>(), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("published");
        
        var testEvent = new TestEvent { EventId = "test-123" };
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            PublisherId = "other-agent",
            Payload = Any.Pack(testEvent)
        };
        
        // Act - Should not throw
        await agent.HandleEventAsync(envelope);
        
        // Assert
        agent.SuccessfulHandlerCount.Should().Be(1);
    }
    
    [Fact(DisplayName = "GetAllSubscribedEventsAsync should return all subscribed event types")]
    public async Task GetAllSubscribedEventsAsync_ShouldReturnAllSubscribedEventTypes()
    {
        // Arrange
        var agent = new TestAgentWithMultipleHandlers();
        
        // Act
        var eventTypes = await agent.GetAllSubscribedEventsAsync();
        
        // Assert
        eventTypes.Should().NotBeNull();
        eventTypes.Should().Contain(typeof(TestEvent));
        eventTypes.Should().Contain(typeof(TestAddItemEvent));
        eventTypes.Should().Contain(typeof(TestUpdateCounterEvent));
        eventTypes.Should().NotContain(typeof(EventEnvelope)); // Excluded by default
    }
    
    [Fact(DisplayName = "GetAllSubscribedEventsAsync should include EventEnvelope when requested")]
    public async Task GetAllSubscribedEventsAsync_ShouldIncludeEventEnvelopeWhenRequested()
    {
        // Arrange
        var agent = new TestAgentWithMultipleHandlers();
        
        // Act
        var eventTypes = await agent.GetAllSubscribedEventsAsync(includeAllEventHandler: true);
        
        // Assert
        eventTypes.Should().Contain(typeof(EventEnvelope));
    }
    
    [Fact(DisplayName = "PrepareResourceContextAsync should delegate to OnPrepareResourceContextAsync")]
    public async Task PrepareResourceContextAsync_ShouldDelegateToOnPrepareResourceContextAsync()
    {
        // Arrange
        var agent = new TestAgentWithResourceHandling();
        var context = new ResourceContext();
        context.AddResource("test-resource", "test-value");
        
        // Act
        await agent.PrepareResourceContextAsync(context);
        
        // Assert
        agent.ResourcePrepared.Should().BeTrue();
        agent.PreparedResourceCount.Should().Be(1);
    }
    
    [Fact(DisplayName = "OnActivateAsync should be callable and log activation")]
    public async Task OnActivateAsync_ShouldBeCallableAndLogActivation()
    {
        // Arrange
        var agent = new TestAgentWithLifecycle(_mockLogger.Object);
        
        // Act
        await agent.OnActivateAsync();
        
        // Assert
        agent.IsActivated.Should().BeTrue();
        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("activated")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
    
    [Fact(DisplayName = "OnDeactivateAsync should be callable and log deactivation")]
    public async Task OnDeactivateAsync_ShouldBeCallableAndLogDeactivation()
    {
        // Arrange
        var agent = new TestAgentWithLifecycle(_mockLogger.Object);
        
        // Act
        await agent.OnDeactivateAsync();
        
        // Assert
        agent.IsDeactivated.Should().BeTrue();
        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("deactivated")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}

// Test helper classes
internal class TestAgentForBase : GAgentBase<TestState>
{
    public TestAgentForBase() : base() { }
    public TestAgentForBase(Guid id) : base(id) { }
    public TestAgentForBase(ILogger logger) : base(logger) { }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Test Agent for Base Tests");
    }
    
    public IEventPublisher? EventPublisher => base.EventPublisher;
    
    public async Task<string> PublishEventAsync<TEvent>(TEvent evt) 
        where TEvent : Google.Protobuf.IMessage
    {
        return await PublishAsync(evt);
    }
}

internal class TestAgentWithMultipleHandlers : GAgentBase<TestState>
{
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Test Agent with Multiple Handlers");
    }
    
    [EventHandler]
    public Task HandleTestEvent(TestEvent evt)
    {
        State.Name = evt.EventData;
        return Task.CompletedTask;
    }
    
    [EventHandler]
    public Task HandleTestAddItem(TestAddItemEvent evt)
    {
        State.Items.Add(evt.ItemName);
        return Task.CompletedTask;
    }
    
    [AllEventHandler]
    public Task HandleAllEvents(EventEnvelope envelope)
    {
        State.Counter++;
        return Task.CompletedTask;
    }
    
    // Default handler pattern
    public Task HandleEventAsync(TestUpdateCounterEvent evt)
    {
        State.Counter += evt.Increment;
        return Task.CompletedTask;
    }
}

internal class TestAgentWithPriorityHandlers : GAgentBase<TestState>
{
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Test Agent with Priority Handlers");
    }
    
    [EventHandler(Priority = -10)]
    public Task HighPriorityHandler(TestEvent evt)
    {
        return Task.CompletedTask;
    }
    
    [EventHandler(Priority = 0)]
    public Task NormalPriorityHandler(TestEvent evt)
    {
        return Task.CompletedTask;
    }
    
    [EventHandler(Priority = 100)]
    public Task LowPriorityHandler(TestEvent evt)
    {
        return Task.CompletedTask;
    }
}

internal class TestAgentWithCounters : GAgentBase<TestState>
{
    public int TestEventCount { get; private set; }
    public int AllEventCount { get; private set; }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Test Agent with Counters");
    }
    
    [EventHandler]
    public Task HandleTestEvent(TestEvent evt)
    {
        TestEventCount++;
        State.Name = evt.EventData;
        return Task.CompletedTask;
    }
    
    [AllEventHandler]
    public Task HandleAll(EventEnvelope envelope)
    {
        AllEventCount++;
        return Task.CompletedTask;
    }
}

internal class TestAgentWithSelfHandling : GAgentBase<TestState>
{
    public int SelfHandlingCount { get; private set; }
    public int NoSelfHandlingCount { get; private set; }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Test Agent with Self Handling");
    }
    
    [EventHandler(AllowSelfHandling = true)]
    public Task HandleWithSelfHandling(TestEvent evt)
    {
        SelfHandlingCount++;
        return Task.CompletedTask;
    }
    
    [EventHandler(AllowSelfHandling = false)]
    public Task HandleWithoutSelfHandling(TestEvent evt)
    {
        NoSelfHandlingCount++;
        return Task.CompletedTask;
    }
}

internal class TestAgentWithFaultyHandler : GAgentBase<TestState>
{
    public int SuccessfulHandlerCount { get; private set; }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Test Agent with Faulty Handler");
    }
    
    [EventHandler(Priority = 1)]
    public Task FaultyHandler(TestEvent evt)
    {
        throw new InvalidOperationException("Simulated handler failure");
    }
    
    [EventHandler(Priority = 2)]
    public Task SuccessfulHandler(TestEvent evt)
    {
        SuccessfulHandlerCount++;
        return Task.CompletedTask;
    }
}

internal class TestAgentWithResourceHandling : GAgentBase<TestState>
{
    public bool ResourcePrepared { get; private set; }
    public int PreparedResourceCount { get; private set; }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Test Agent with Resource Handling");
    }
    
    protected override Task OnPrepareResourceContextAsync(ResourceContext context, CancellationToken ct = default)
    {
        ResourcePrepared = true;
        PreparedResourceCount = context.AvailableResources.Count;
        return Task.CompletedTask;
    }
}

internal class TestAgentWithLifecycle : GAgentBase<TestState>
{
    public bool IsActivated { get; private set; }
    public bool IsDeactivated { get; private set; }
    
    public TestAgentWithLifecycle(ILogger logger) : base(logger) { }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Test Agent with Lifecycle");
    }
    
    public override Task OnActivateAsync(CancellationToken ct = default)
    {
        IsActivated = true;
        return base.OnActivateAsync(ct);
    }
    
    public override Task OnDeactivateAsync(CancellationToken ct = default)
    {
        IsDeactivated = true;
        return base.OnDeactivateAsync(ct);
    }
}
