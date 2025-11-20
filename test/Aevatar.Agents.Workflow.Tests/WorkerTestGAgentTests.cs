using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Workflow;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Aevatar.Agents.Workflow.Tests;

public class WorkerTestGAgentTests
{
    private readonly Mock<ILogger<WorkerTestGAgent>> _mockLogger;
    private readonly Mock<IEventPublisher> _mockEventPublisher;

    public WorkerTestGAgentTests()
    {
        _mockLogger = new Mock<ILogger<WorkerTestGAgent>>();
        _mockEventPublisher = new Mock<IEventPublisher>();
    }

    private WorkerTestGAgent CreateAgent(Guid? id = null)
    {
        var agentId = id ?? Guid.NewGuid();
        var agent = new WorkerTestGAgent(agentId, _mockLogger.Object);
        agent.SetEventPublisher(_mockEventPublisher.Object);
        return agent;
    }

    [Fact(DisplayName = "WorkerTestGAgent should initialize with correct state")]
    public async Task WorkerTestGAgent_ShouldInitializeWithCorrectState()
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
        state.FailureSummary.Should().BeEmpty();
    }

    [Fact(DisplayName = "ChatAsync should return configured input when no failure summary")]
    public async Task ChatAsync_ShouldReturnConfiguredInputWhenNoFailureSummary()
    {
        // Arrange
        var agent = CreateAgent();
        var setInputEvent = new SetInputEvent
        {
            Input = "Hello World",
            Reason = "Test"
        };

        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(setInputEvent)
        };

        await agent.HandleEventAsync(envelope);

        // Act
        var result = await agent.ChatAsync();

        // Assert
        result.Should().Be("Hello World");
    }

    [Fact(DisplayName = "ChatAsync should throw exception when failure summary is set")]
    public async Task ChatAsync_ShouldThrowExceptionWhenFailureSummaryIsSet()
    {
        // Arrange
        var agent = CreateAgent();
        var failureEvent = new SetFailureSummaryEvent
        {
            FailureSummary = "Test failure"
        };

        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(failureEvent)
        };

        await agent.HandleEventAsync(envelope);

        // Act & Assert
        var act = async () => await agent.ChatAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Test failure");
    }

    [Fact(DisplayName = "HandleSetFailureSummaryEvent should update failure summary")]
    public async Task HandleSetFailureSummaryEvent_ShouldUpdateFailureSummary()
    {
        // Arrange
        var agent = CreateAgent();
        var failureEvent = new SetFailureSummaryEvent
        {
            FailureSummary = "Test failure summary"
        };

        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(failureEvent)
        };

        // Act
        await agent.HandleEventAsync(envelope);

        // Assert
        var state = agent.GetState();
        state.FailureSummary.Should().Be("Test failure summary");
        agent.GetFailureSummary().Should().Be("Test failure summary");
    }

    [Fact(DisplayName = "OnConfigureAsync should set failure summary from configuration")]
    public async Task OnConfigureAsync_ShouldSetFailureSummaryFromConfiguration()
    {
        // Arrange
        var agent = CreateAgent();
        var config = new WorkerTestConfiguration
        {
            Input = "Test Input",
            FailureSummary = "Configuration failure"
        };

        // Act
        await agent.ConfigureAsync(config);

        // Assert
        var state = agent.GetState();
        state.FailureSummary.Should().Be("Configuration failure");
        agent.GetFailureSummary().Should().Be("Configuration failure");
    }

    [Fact(DisplayName = "ChatAsync should throw exception after configuring with failure summary")]
    public async Task ChatAsync_ShouldThrowExceptionAfterConfiguringWithFailureSummary()
    {
        // Arrange
        var agent = CreateAgent();
        var config = new WorkerTestConfiguration
        {
            Input = "Test Input",
            FailureSummary = "Test failure from config"
        };

        await agent.ConfigureAsync(config);

        // Act & Assert
        var act = async () => await agent.ChatAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Test failure from config");
    }

    [Fact(DisplayName = "ChatAsync should return member name format when member name is set")]
    public async Task ChatAsync_ShouldReturnMemberNameFormatWhenMemberNameIsSet()
    {
        // Arrange
        var agent = CreateAgent();
        var config = new WorkerTestConfiguration
        {
            Input = "Test Input",
            MemberName = "TestMember"
        };

        await agent.ConfigureAsync(config);

        // Act
        var response = await agent.ChatAsync();

        // Assert
        response.Should().Be("TestMember Send the message");
    }

    [Fact(DisplayName = "GetFailureSummary should return current failure summary")]
    public void GetFailureSummary_ShouldReturnCurrentFailureSummary()
    {
        // Arrange
        var agent = CreateAgent();
        var state = agent.GetState();
        state.FailureSummary = "Test failure";

        // Act
        var result = agent.GetFailureSummary();

        // Assert
        result.Should().Be("Test failure");
    }

    [Fact(DisplayName = "GetMemberName should return current member name")]
    public void GetMemberName_ShouldReturnCurrentMemberName()
    {
        // Arrange
        var agent = CreateAgent();
        var state = agent.GetState();
        state.MemberName = "TestMember";

        // Act
        var result = agent.GetMemberName();

        // Assert
        result.Should().Be("TestMember");
    }
}

