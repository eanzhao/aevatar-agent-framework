using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Core.EventSourcing;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Microsoft.Extensions.Logging;
using Moq;

namespace Aevatar.Agents.Core.Tests.EventSourcing;

public class GAgentBaseWithEventSourcingTests
{
    private readonly Mock<IEventStore> _mockEventStore;
    private readonly Mock<ILogger<TestEventSourcingAgent>> _mockLogger;
    
    public GAgentBaseWithEventSourcingTests()
    {
        _mockEventStore = new Mock<IEventStore>();
        _mockLogger = new Mock<ILogger<TestEventSourcingAgent>>();
    }
    
    [Fact]
    public async Task RaiseStateChangeEventAsync_Should_PersistEvent_And_ApplyToState()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var agent = new TestEventSourcingAgent(agentId, _mockEventStore.Object, _mockLogger.Object);
        var testEvent = new TestEvent { Value = 100 };
        
        _mockEventStore.Setup(x => x.SaveEventAsync(
            It.IsAny<Guid>(), 
            It.IsAny<StateLogEvent>(), 
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        
        // Act
        await agent.RaiseTestEvent(testEvent);
        
        // Assert
        Assert.Equal(100, agent.GetState().Total);
        Assert.Equal(1, agent.GetCurrentVersion());
        
        _mockEventStore.Verify(x => x.SaveEventAsync(
            agentId, 
            It.Is<StateLogEvent>(e => e.Version == 1 && e.AgentId == agentId),
            It.IsAny<CancellationToken>()), 
            Times.Once);
    }
    
    [Fact]
    public async Task OnActivateAsync_Should_ReplayEvents()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var agent = new TestEventSourcingAgent(agentId, _mockEventStore.Object, _mockLogger.Object);
        
        var events = new List<StateLogEvent>
        {
            CreateTestLogEvent(agentId, 1, 50),
            CreateTestLogEvent(agentId, 2, 30),
            CreateTestLogEvent(agentId, 3, 20)
        };
        
        _mockEventStore.Setup(x => x.GetEventsAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(events);
        
        // Act
        await agent.OnActivateAsync();
        
        // Assert
        Assert.Equal(100, agent.GetState().Total); // 50 + 30 + 20
        Assert.Equal(3, agent.GetCurrentVersion());
    }
    
    [Fact]
    public async Task OnActivateAsync_Should_HandleEmptyEventStore()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var agent = new TestEventSourcingAgent(agentId, _mockEventStore.Object, _mockLogger.Object);
        
        _mockEventStore.Setup(x => x.GetEventsAsync(agentId, 0, int.MaxValue, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StateLogEvent>());
        
        // Act
        await agent.OnActivateAsync();
        
        // Assert
        Assert.Equal(0, agent.GetState().Total);
        Assert.Equal(0, agent.GetCurrentVersion());
    }
    
    [Fact]
    public async Task Snapshot_Should_BeCreated_At_Interval()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var agent = new TestEventSourcingAgent(agentId, _mockEventStore.Object, _mockLogger.Object);
        // SnapshotInterval is hardcoded to 100 in GAgentBaseWithEventSourcing
        
        _mockEventStore.Setup(x => x.SaveEventAsync(
            It.IsAny<Guid>(), 
            It.IsAny<StateLogEvent>(), 
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        
        // Act - Raise 100 events to trigger snapshot
        for (int i = 0; i < 100; i++)
        {
            await agent.RaiseTestEvent(new TestEvent { Value = i + 1 });
        }
        
        // Assert
        Assert.Equal(100, agent.GetCurrentVersion());
        Assert.True(agent.SnapshotCreated);
    }
    
    [Fact]
    public async Task ReplayEventsAsync_Should_SkipUnknownEventTypes()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var agent = new TestEventSourcingAgent(agentId, _mockEventStore.Object, _mockLogger.Object);
        
        var events = new List<StateLogEvent>
        {
            CreateTestLogEvent(agentId, 1, 50),
            new StateLogEvent // Unknown event type
            {
                EventId = Guid.NewGuid(),
                AgentId = agentId,
                Version = 2,
                EventType = "UnknownType",
                EventData = new byte[0],
                TimestampUtc = DateTime.UtcNow
            },
            CreateTestLogEvent(agentId, 3, 30)
        };
        
        _mockEventStore.Setup(x => x.GetEventsAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(events);
        
        // Act
        await agent.OnActivateAsync();
        
        // Assert
        Assert.Equal(80, agent.GetState().Total); // 50 + 30 (unknown skipped)
        Assert.Equal(3, agent.GetCurrentVersion()); // Version still increments
    }
    
    private StateLogEvent CreateTestLogEvent(Guid agentId, long version, int value)
    {
        var testEvent = new TestEvent { Value = value };
        using var stream = new MemoryStream();
        using var output = new CodedOutputStream(stream);
        testEvent.WriteTo(output);
        output.Flush();
        
        return new StateLogEvent
        {
            EventId = Guid.NewGuid(),
            AgentId = agentId,
            Version = version,
            EventType = typeof(TestEvent).AssemblyQualifiedName!,
            EventData = stream.ToArray(),
            TimestampUtc = DateTime.UtcNow
        };
    }
}

// Test implementations
public class TestState
{
    public int Total { get; set; }
}

// Simple test event without full Protobuf implementation
public class TestEvent : IMessage
{
    public int Value { get; set; }
    
    // Mock static parser for testing
    public static object Parser { get; } = new TestEventParser();
    
    public MessageDescriptor Descriptor => null!;
    
    public int CalculateSize() => 4;
    
    public void MergeFrom(CodedInputStream input) 
    {
        Value = input.ReadInt32();
    }
    
    public void WriteTo(CodedOutputStream output)
    {
        output.WriteInt32(Value);
    }
}

// Mock parser class for testing
public class TestEventParser
{
    public TestEvent ParseFrom(byte[] data)
    {
        using var input = new CodedInputStream(data);
        var evt = new TestEvent();
        evt.MergeFrom(input);
        return evt;
    }
}

public class TestEventSourcingAgent : GAgentBaseWithEventSourcing<TestState>
{
    public bool SnapshotCreated { get; private set; }
    private int _snapshotInterval = 100;
    
    public TestEventSourcingAgent(Guid id, IEventStore? eventStore = null, ILogger<TestEventSourcingAgent>? logger = null)
        : base(id, eventStore, logger)
    {
    }
    
    public new async Task OnActivateAsync()
    {
        await base.OnActivateAsync();
    }
    
    public void SetSnapshotInterval(int interval)
    {
        _snapshotInterval = interval;
    }
    
    // SnapshotInterval is not overridable in current implementation
    // protected override int SnapshotInterval => _snapshotInterval;
    
    public async Task RaiseTestEvent(TestEvent evt)
    {
        await RaiseStateChangeEventAsync(evt);
    }
    
    protected override Task ApplyStateChangeEventAsync<TEvent>(TEvent evt, CancellationToken ct = default)
    {
        if (evt is TestEvent testEvent)
        {
            _state.Total += testEvent.Value;
        }
        return Task.CompletedTask;
    }
    
    protected override Task CreateSnapshotAsync(CancellationToken ct = default)
    {
        SnapshotCreated = true;
        return base.CreateSnapshotAsync(ct);
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Test EventSourcing Agent");
    }
}
