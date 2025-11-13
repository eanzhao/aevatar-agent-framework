using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Workflow;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Aevatar.Agents.Workflow.Tests;

public class InputGAgentTests
{
    private readonly Mock<ILogger<InputGAgent>> _mockLogger;
    private readonly Mock<IEventPublisher> _mockEventPublisher;

    public InputGAgentTests()
    {
        _mockLogger = new Mock<ILogger<InputGAgent>>();
        _mockEventPublisher = new Mock<IEventPublisher>();
    }

    private InputGAgent CreateAgent(Guid? id = null)
    {
        var agentId = id ?? Guid.NewGuid();
        var agent = new InputGAgent(agentId, _mockLogger.Object);
        agent.SetEventPublisher(_mockEventPublisher.Object);
        return agent;
    }

    [Fact(DisplayName = "InputGAgent should initialize with correct state")]
    public async Task InputGAgent_ShouldInitializeWithCorrectState()
    {
        // Arrange & Act
        var agent = CreateAgent();
        await agent.OnActivateAsync();

        // Assert
        agent.Id.Should().NotBe(Guid.Empty);
        var state = agent.GetState();
        state.Should().NotBeNull();
        state.AgentId.Should().Be(agent.Id.ToString());
        state.Input.Should().BeEmpty();
        state.UpdateCount.Should().Be(0);
    }

    [Fact(DisplayName = "GetDescriptionAsync should return correct description")]
    public async Task GetDescriptionAsync_ShouldReturnCorrectDescription()
    {
        // Arrange
        var agent = CreateAgent();

        // Act
        var description = await agent.GetDescriptionAsync();

        // Assert
        description.Should().Contain("Input Agent");
        description.Should().Contain(agent.Id.ToString());
    }

    [Fact(DisplayName = "GetDescriptionAsync should show current input")]
    public async Task GetDescriptionAsync_ShouldShowCurrentInput()
    {
        // Arrange
        var agent = CreateAgent();
        var setInputEvent = new SetInputEvent
        {
            Input = "Test Input",
            Reason = "Test"
        };

        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(setInputEvent)
        };

        await agent.HandleEventAsync(envelope);

        // Act
        var description = await agent.GetDescriptionAsync();

        // Assert
        description.Should().Contain("Test Input");
    }

    [Fact(DisplayName = "HandleSetInputEvent should update input value")]
    public async Task HandleSetInputEvent_ShouldUpdateInputValue()
    {
        // Arrange
        var agent = CreateAgent();
        var setInputEvent = new SetInputEvent
        {
            Input = "Hello World",
            Reason = "Test update"
        };

        _mockEventPublisher
            .Setup(p => p.PublishEventAsync(
                It.IsAny<SetInputEvent>(),
                EventDirection.Down,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("event-id");

        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(setInputEvent)
        };

        // Act
        await agent.HandleEventAsync(envelope);

        // Assert
        var state = agent.GetState();
        state.Input.Should().Be("Hello World");
        state.UpdateCount.Should().Be(1);
        state.LastUpdated.Should().NotBeNull();
        agent.GetInput().Should().Be("Hello World");
        agent.GetUpdateCount().Should().Be(1);

        _mockEventPublisher.Verify(
            p => p.PublishEventAsync(
                It.Is<SetInputEvent>(e => e.Input == "Hello World"),
                EventDirection.Down,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact(DisplayName = "HandleSetInputEvent should increment update count")]
    public async Task HandleSetInputEvent_ShouldIncrementUpdateCount()
    {
        // Arrange
        var agent = CreateAgent();
        _mockEventPublisher
            .Setup(p => p.PublishEventAsync(
                It.IsAny<SetInputEvent>(),
                EventDirection.Down,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("event-id");

        var setInputEvent1 = new SetInputEvent { Input = "First", Reason = "Test 1" };
        var setInputEvent2 = new SetInputEvent { Input = "Second", Reason = "Test 2" };
        var setInputEvent3 = new SetInputEvent { Input = "Third", Reason = "Test 3" };

        var envelope1 = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(setInputEvent1)
        };
        var envelope2 = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(setInputEvent2)
        };
        var envelope3 = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(setInputEvent3)
        };

        // Act
        await agent.HandleEventAsync(envelope1);
        await agent.HandleEventAsync(envelope2);
        await agent.HandleEventAsync(envelope3);

        // Assert
        var state = agent.GetState();
        state.UpdateCount.Should().Be(3);
        state.Input.Should().Be("Third"); // Last update
        agent.GetUpdateCount().Should().Be(3);
    }

    [Fact(DisplayName = "HandleSetInputEvent should update last updated timestamp")]
    public async Task HandleSetInputEvent_ShouldUpdateLastUpdatedTimestamp()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.OnActivateAsync();

        var initialTimestamp = agent.GetState().LastUpdated;

        // Wait a bit to ensure timestamp difference
        await Task.Delay(100);

        _mockEventPublisher
            .Setup(p => p.PublishEventAsync(
                It.IsAny<SetInputEvent>(),
                EventDirection.Down,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("event-id");

        var setInputEvent = new SetInputEvent
        {
            Input = "Updated",
            Reason = "Test"
        };

        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(setInputEvent)
        };

        // Act
        await agent.HandleEventAsync(envelope);

        // Assert
        var state = agent.GetState();
        state.LastUpdated.Should().NotBeNull();
        state.LastUpdated.Should().NotBe(initialTimestamp);
    }

    [Fact(DisplayName = "OnConfigureAsync should set input from configuration")]
    public async Task OnConfigureAsync_ShouldSetInputFromConfiguration()
    {
        // Arrange
        var agent = CreateAgent();
        var config = new InputConfiguration
        {
            Input = "Configured Input",
            Description = "Configuration description"
        };

        _mockEventPublisher
            .Setup(p => p.PublishEventAsync(
                It.IsAny<SetInputEvent>(),
                EventDirection.Down,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("event-id");

        // Act
        await agent.ConfigureAsync(config);

        // Assert
        var state = agent.GetState();
        state.Input.Should().Be("Configured Input");
        state.UpdateCount.Should().Be(1);
        agent.GetInput().Should().Be("Configured Input");

        _mockEventPublisher.Verify(
            p => p.PublishEventAsync(
                It.Is<SetInputEvent>(e => 
                    e.Input == "Configured Input" && 
                    e.Reason == "Configuration description"),
                EventDirection.Down,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact(DisplayName = "OnConfigureAsync should use default reason when description is empty")]
    public async Task OnConfigureAsync_ShouldUseDefaultReasonWhenDescriptionIsEmpty()
    {
        // Arrange
        var agent = CreateAgent();
        var config = new InputConfiguration
        {
            Input = "Test Input",
            Description = string.Empty // Empty string
        };

        _mockEventPublisher
            .Setup(p => p.PublishEventAsync(
                It.IsAny<SetInputEvent>(),
                EventDirection.Down,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("event-id");

        // Act
        await agent.ConfigureAsync(config);

        // Assert
        // Verify that the event was published with default reason
        _mockEventPublisher.Verify(
            p => p.PublishEventAsync(
                It.Is<SetInputEvent>(e => 
                    e.Input == "Test Input" && 
                    e.Reason == "Configuration update"),
                EventDirection.Down,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact(DisplayName = "GetInput should return current input value")]
    public void GetInput_ShouldReturnCurrentInputValue()
    {
        // Arrange
        var agent = CreateAgent();
        var state = agent.GetState();
        state.Input = "Current Input";

        // Act
        var result = agent.GetInput();

        // Assert
        result.Should().Be("Current Input");
    }

    [Fact(DisplayName = "GetUpdateCount should return current update count")]
    public void GetUpdateCount_ShouldReturnCurrentUpdateCount()
    {
        // Arrange
        var agent = CreateAgent();
        var state = agent.GetState();
        state.UpdateCount = 5;

        // Act
        var result = agent.GetUpdateCount();

        // Assert
        result.Should().Be(5);
    }

    [Fact(DisplayName = "HandleSetInputEvent should handle empty input")]
    public async Task HandleSetInputEvent_ShouldHandleEmptyInput()
    {
        // Arrange
        var agent = CreateAgent();
        var setInputEvent = new SetInputEvent
        {
            Input = string.Empty,
            Reason = "Clear input"
        };

        _mockEventPublisher
            .Setup(p => p.PublishEventAsync(
                It.IsAny<SetInputEvent>(),
                EventDirection.Down,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("event-id");

        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(setInputEvent)
        };

        // Act
        await agent.HandleEventAsync(envelope);

        // Assert
        var state = agent.GetState();
        state.Input.Should().BeEmpty();
        state.UpdateCount.Should().Be(1);
    }

    [Fact(DisplayName = "HandleSetInputEvent should publish event with correct direction")]
    public async Task HandleSetInputEvent_ShouldPublishEventWithCorrectDirection()
    {
        // Arrange
        var agent = CreateAgent();
        var setInputEvent = new SetInputEvent
        {
            Input = "Test",
            Reason = "Test"
        };

        _mockEventPublisher
            .Setup(p => p.PublishEventAsync(
                It.IsAny<SetInputEvent>(),
                EventDirection.Down,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("event-id");

        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(setInputEvent)
        };

        // Act
        await agent.HandleEventAsync(envelope);

        // Assert
        _mockEventPublisher.Verify(
            p => p.PublishEventAsync(
                It.IsAny<SetInputEvent>(),
                EventDirection.Down, // Should publish DOWN
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact(DisplayName = "Multiple configurations should update input multiple times")]
    public async Task MultipleConfigurations_ShouldUpdateInputMultipleTimes()
    {
        // Arrange
        var agent = CreateAgent();
        _mockEventPublisher
            .Setup(p => p.PublishEventAsync(
                It.IsAny<SetInputEvent>(),
                EventDirection.Down,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("event-id");

        var config1 = new InputConfiguration { Input = "First", Description = "First config" };
        var config2 = new InputConfiguration { Input = "Second", Description = "Second config" };
        var config3 = new InputConfiguration { Input = "Third", Description = "Third config" };

        // Act
        await agent.ConfigureAsync(config1);
        await agent.ConfigureAsync(config2);
        await agent.ConfigureAsync(config3);

        // Assert
        var state = agent.GetState();
        state.Input.Should().Be("Third");
        state.UpdateCount.Should().Be(3);
        agent.GetInput().Should().Be("Third");
        agent.GetUpdateCount().Should().Be(3);
    }
}
