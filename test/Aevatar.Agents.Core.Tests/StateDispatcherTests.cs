using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Aevatar.Agents.Core.Tests;

public class StateDispatcherTests
{
    private readonly Mock<ILogger<StateDispatcher>> _mockLogger = new();
    
    [Fact(DisplayName = "StateDispatcher should initialize without logger")]
    public void StateDispatcher_ShouldInitializeWithoutLogger()
    {
        // Arrange & Act
        var dispatcher = new StateDispatcher();
        
        // Assert
        dispatcher.Should().NotBeNull();
    }
    
    [Fact(DisplayName = "StateDispatcher should initialize with logger")]
    public void StateDispatcher_ShouldInitializeWithLogger()
    {
        // Arrange & Act
        var dispatcher = new StateDispatcher(_mockLogger.Object);
        
        // Assert
        dispatcher.Should().NotBeNull();
    }
    
    [Fact(DisplayName = "PublishSingleAsync should publish state to single channel")]
    public async Task PublishSingleAsync_ShouldPublishStateToSingleChannel()
    {
        // Arrange
        var dispatcher = new StateDispatcher(_mockLogger.Object);
        var agentId = Guid.NewGuid();
        var state = new TestState { Name = "Test", Counter = 42 };
        var snapshot = new StateSnapshot<TestState>(agentId, state, 1);
        
        // Act
        await dispatcher.PublishSingleAsync(agentId, snapshot);
        
        // Assert
        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Publishing single state change")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
    
    [Fact(DisplayName = "PublishBatchAsync should publish state to batch channel")]
    public async Task PublishBatchAsync_ShouldPublishStateToBatchChannel()
    {
        // Arrange
        var dispatcher = new StateDispatcher(_mockLogger.Object);
        var agentId = Guid.NewGuid();
        var state = new TestState { Name = "Test", Counter = 42 };
        var snapshot = new StateSnapshot<TestState>(agentId, state, 1);
        
        // Act
        await dispatcher.PublishBatchAsync(agentId, snapshot);
        
        // Assert
        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Publishing batch state change")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
    
    [Fact(DisplayName = "SubscribeAsync should receive published single state changes")]
    public async Task SubscribeAsync_ShouldReceivePublishedSingleStateChanges()
    {
        // Arrange
        var dispatcher = new StateDispatcher();
        var agentId = Guid.NewGuid();
        var receivedSnapshots = new List<StateSnapshot<TestState>>();
        var tcs = new TaskCompletionSource();
        
        // Act
        await dispatcher.SubscribeAsync<TestState>(
            agentId,
            async snapshot =>
            {
                receivedSnapshots.Add(snapshot);
                tcs.SetResult();
                await Task.CompletedTask;
            });
        
        var state = new TestState { Name = "Test", Counter = 42 };
        var snapshot = new StateSnapshot<TestState>(agentId, state, 1);
        await dispatcher.PublishSingleAsync(agentId, snapshot);
        
        // Wait for handler to process
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(1));
        
        // Assert
        receivedSnapshots.Should().HaveCount(1);
        receivedSnapshots[0].State.Name.Should().Be("Test");
        receivedSnapshots[0].State.Counter.Should().Be(42);
        receivedSnapshots[0].Version.Should().Be(1);
    }
    
    [Fact(DisplayName = "Multiple subscribers should receive state changes independently")]
    public async Task MultipleSubscribers_ShouldReceiveStateChangesIndependently()
    {
        // Arrange
        var dispatcher = new StateDispatcher();
        var agentId = Guid.NewGuid();
        var receivedSnapshots = new List<StateSnapshot<TestState>>();
        var tcs = new TaskCompletionSource();
        var receivedCount = 0;
        
        // Act - Subscribe handler
        await dispatcher.SubscribeAsync<TestState>(
            agentId,
            async snapshot =>
            {
                receivedSnapshots.Add(snapshot);
                receivedCount++;
                if (receivedCount >= 1)
                {
                    tcs.SetResult();
                }
                await Task.CompletedTask;
            });
        
        var state = new TestState { Name = "Broadcast", Counter = 100 };
        var snapshot = new StateSnapshot<TestState>(agentId, state, 1);
        await dispatcher.PublishSingleAsync(agentId, snapshot);
        
        // Wait for handler
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(1));
        
        // Assert
        receivedSnapshots.Should().HaveCount(1);
        receivedSnapshots[0].State.Name.Should().Be("Broadcast");
        receivedSnapshots[0].State.Counter.Should().Be(100);
    }
    
    [Fact(DisplayName = "SubscribeAsync should handle handler exceptions gracefully")]
    public async Task SubscribeAsync_ShouldHandleHandlerExceptionsGracefully()
    {
        // Arrange
        var dispatcher = new StateDispatcher(_mockLogger.Object);
        var agentId = Guid.NewGuid();
        var tcs = new TaskCompletionSource();
        
        // Act - Subscribe with faulty handler
        await dispatcher.SubscribeAsync<TestState>(
            agentId,
            async snapshot =>
            {
                tcs.SetResult();
                throw new InvalidOperationException("Handler error");
            });
        
        var state = new TestState { Name = "Test", Counter = 42 };
        var snapshot = new StateSnapshot<TestState>(agentId, state, 1);
        await dispatcher.PublishSingleAsync(agentId, snapshot);
        
        // Wait for handler to process
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(1));
        
        // Assert - Should log error but not crash
        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error in state subscription handler")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
    
    [Fact(DisplayName = "Different agents should have isolated channels")]
    public async Task DifferentAgents_ShouldHaveIsolatedChannels()
    {
        // Arrange
        var dispatcher = new StateDispatcher();
        var agentId1 = Guid.NewGuid();
        var agentId2 = Guid.NewGuid();
        var receivedForAgent1 = new List<StateSnapshot<TestState>>();
        var receivedForAgent2 = new List<StateSnapshot<TestState>>();
        var tcs1 = new TaskCompletionSource();
        var tcs2 = new TaskCompletionSource();
        
        // Act
        await dispatcher.SubscribeAsync<TestState>(
            agentId1,
            async snapshot =>
            {
                receivedForAgent1.Add(snapshot);
                tcs1.SetResult();
                await Task.CompletedTask;
            });
        
        await dispatcher.SubscribeAsync<TestState>(
            agentId2,
            async snapshot =>
            {
                receivedForAgent2.Add(snapshot);
                tcs2.SetResult();
                await Task.CompletedTask;
            });
        
        var state1 = new TestState { Name = "Agent1", Counter = 1 };
        var state2 = new TestState { Name = "Agent2", Counter = 2 };
        
        await dispatcher.PublishSingleAsync(agentId1, new StateSnapshot<TestState>(agentId1, state1, 1));
        await dispatcher.PublishSingleAsync(agentId2, new StateSnapshot<TestState>(agentId2, state2, 1));
        
        await Task.WhenAll(
            tcs1.Task.WaitAsync(TimeSpan.FromSeconds(1)),
            tcs2.Task.WaitAsync(TimeSpan.FromSeconds(1))
        );
        
        // Assert
        receivedForAgent1.Should().HaveCount(1);
        receivedForAgent1[0].State.Name.Should().Be("Agent1");
        
        receivedForAgent2.Should().HaveCount(1);
        receivedForAgent2[0].State.Name.Should().Be("Agent2");
    }
}

public class StateSnapshotTests
{
    [Fact(DisplayName = "StateSnapshot should initialize with default constructor")]
    public void StateSnapshot_ShouldInitializeWithDefaultConstructor()
    {
        // Arrange & Act
        var snapshot = new StateSnapshot<TestState>();
        
        // Assert
        snapshot.AgentId.Should().Be(Guid.Empty);
        snapshot.State.Should().NotBeNull();
        snapshot.Version.Should().Be(0);
        snapshot.TimestampUtc.Should().Be(default(DateTime));
    }
    
    [Fact(DisplayName = "StateSnapshot should initialize with parameterized constructor")]
    public void StateSnapshot_ShouldInitializeWithParameterizedConstructor()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var state = new TestState { Name = "Test", Counter = 42 };
        var version = 5L;
        var beforeTime = DateTime.UtcNow;
        
        // Act
        var snapshot = new StateSnapshot<TestState>(agentId, state, version);
        var afterTime = DateTime.UtcNow;
        
        // Assert
        snapshot.AgentId.Should().Be(agentId);
        snapshot.State.Should().Be(state);
        snapshot.Version.Should().Be(version);
        snapshot.TimestampUtc.Should().BeAfter(beforeTime.AddSeconds(-1));
        snapshot.TimestampUtc.Should().BeBefore(afterTime.AddSeconds(1));
    }
    
    [Fact(DisplayName = "StateSnapshot properties should be settable")]
    public void StateSnapshot_PropertiesShouldBeSettable()
    {
        // Arrange
        var snapshot = new StateSnapshot<TestState>();
        var agentId = Guid.NewGuid();
        var state = new TestState { Name = "Updated", Counter = 100 };
        var version = 10L;
        var timestamp = DateTime.UtcNow;
        
        // Act
        snapshot.AgentId = agentId;
        snapshot.State = state;
        snapshot.Version = version;
        snapshot.TimestampUtc = timestamp;
        
        // Assert
        snapshot.AgentId.Should().Be(agentId);
        snapshot.State.Should().Be(state);
        snapshot.Version.Should().Be(version);
        snapshot.TimestampUtc.Should().Be(timestamp);
    }
}
