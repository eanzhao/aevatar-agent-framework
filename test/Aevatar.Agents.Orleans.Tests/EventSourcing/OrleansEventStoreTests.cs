using Aevatar.Agents;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Orleans.EventSourcing;
using Aevatar.Agents.TestBase;
using Google.Protobuf.WellKnownTypes;
using Xunit;

namespace Aevatar.Agents.Orleans.Tests.EventSourcing;

/// <summary>
/// OrleansEventStore integration tests
/// Tests the Orleans-based EventStore implementation using TestCluster
/// </summary>
public class OrleansEventStoreTests : AevatarAgentsTestBase
{
    public OrleansEventStoreTests(ClusterFixture fixture) : base(fixture)
    {
    }

    private AgentStateEvent CreateTestEvent(Guid agentId, long version, string eventType)
    {
        return new AgentStateEvent
        {
            EventId = Guid.NewGuid().ToString(),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Version = version,
            EventType = eventType,
            EventData = Google.Protobuf.WellKnownTypes.Any.Pack(new ChildAddedEvent { ChildId = $"child-{version}" }),
            AgentId = agentId.ToString(),
            CorrelationId = Guid.NewGuid().ToString(),
            Metadata = { { "testKey", $"testValue-{version}" } }
        };
    }

    private AgentSnapshot CreateTestSnapshot(Guid agentId, long version)
    {
        return new AgentSnapshot
        {
            Version = version,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            StateData = Google.Protobuf.WellKnownTypes.Any.Pack(new LLMAgentState 
            { 
                CurrentVersion = version,
                LlmConfig = $"config-{version}"
            }),
            Metadata = { { "snapshotKey", $"snapshotValue-{version}" } }
        };
    }

    [Fact(DisplayName = "OrleansEventStore should append events successfully")]
    public async Task AppendEventsAsync_ShouldAppendEvents()
    {
        // Arrange
        var eventStore = new OrleansEventStore(GrainFactory);
        var agentId = Guid.NewGuid();
        var events = new List<AgentStateEvent>
        {
            CreateTestEvent(agentId, 1, "Event1"),
            CreateTestEvent(agentId, 2, "Event2")
        };

        // Act
        var newVersion = await eventStore.AppendEventsAsync(agentId, events, 0);

        // Assert
        Assert.Equal(2, newVersion);
        var storedEvents = await eventStore.GetEventsAsync(agentId);
        Assert.Equal(2, storedEvents.Count);
        Assert.Equal(events.First().EventId, storedEvents.First().EventId);
    }

    [Fact(DisplayName = "OrleansEventStore should enforce optimistic concurrency")]
    public async Task AppendEventsAsync_ShouldEnforceOptimisticConcurrency()
    {
        // Arrange
        var eventStore = new OrleansEventStore(GrainFactory);
        var agentId = Guid.NewGuid();
        await eventStore.AppendEventsAsync(agentId, new[] { CreateTestEvent(agentId, 1, "Event1") }, 0);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            eventStore.AppendEventsAsync(agentId, new[] { CreateTestEvent(agentId, 2, "Event2") }, 0));

        Assert.Contains("Concurrency conflict", exception.Message);
    }

    [Fact(DisplayName = "OrleansEventStore should support range queries")]
    public async Task GetEventsAsync_ShouldSupportRangeQuery()
    {
        // Arrange
        var eventStore = new OrleansEventStore(GrainFactory);
        var agentId = Guid.NewGuid();
        var events = new List<AgentStateEvent>
        {
            CreateTestEvent(agentId, 1, "Event1"),
            CreateTestEvent(agentId, 2, "Event2"),
            CreateTestEvent(agentId, 3, "Event3"),
            CreateTestEvent(agentId, 4, "Event4")
        };
        await eventStore.AppendEventsAsync(agentId, events, 0);

        // Act
        var rangeEvents = await eventStore.GetEventsAsync(agentId, fromVersion: 2, toVersion: 3);

        // Assert
        Assert.Equal(2, rangeEvents.Count);
        Assert.Equal(2L, rangeEvents.First().Version);
        Assert.Equal(3L, rangeEvents.Last().Version);
    }

    [Fact(DisplayName = "OrleansEventStore should get latest version")]
    public async Task GetLatestVersionAsync_ShouldReturnLatestVersion()
    {
        // Arrange
        var eventStore = new OrleansEventStore(GrainFactory);
        var agentId = Guid.NewGuid();
        var events = new List<AgentStateEvent>
        {
            CreateTestEvent(agentId, 1, "Event1"),
            CreateTestEvent(agentId, 2, "Event2"),
            CreateTestEvent(agentId, 3, "Event3")
        };
        await eventStore.AppendEventsAsync(agentId, events, 0);

        // Act
        var latestVersion = await eventStore.GetLatestVersionAsync(agentId);

        // Assert
        Assert.Equal(3, latestVersion);
    }

    [Fact(DisplayName = "OrleansEventStore should save and retrieve snapshot")]
    public async Task SaveSnapshotAsync_ShouldSaveSnapshot()
    {
        // Arrange
        var eventStore = new OrleansEventStore(GrainFactory);
        var agentId = Guid.NewGuid();
        var snapshot = CreateTestSnapshot(agentId, 5);

        // Act
        await eventStore.SaveSnapshotAsync(agentId, snapshot);
        var retrievedSnapshot = await eventStore.GetLatestSnapshotAsync(agentId);

        // Assert
        Assert.NotNull(retrievedSnapshot);
        Assert.Equal(5, retrievedSnapshot.Version);
        Assert.Equal("snapshotValue-5", retrievedSnapshot.Metadata["snapshotKey"]);
    }

    [Fact(DisplayName = "OrleansEventStore should return 0 for non-existent agent")]
    public async Task GetLatestVersionAsync_ShouldReturn0ForNonExistentAgent()
    {
        // Arrange
        var eventStore = new OrleansEventStore(GrainFactory);
        var agentId = Guid.NewGuid();

        // Act
        var version = await eventStore.GetLatestVersionAsync(agentId);

        // Assert
        Assert.Equal(0, version);
    }

    [Fact(DisplayName = "OrleansEventStore should return null for non-existent snapshot")]
    public async Task GetLatestSnapshotAsync_ShouldReturnNullForNonExistentSnapshot()
    {
        // Arrange
        var eventStore = new OrleansEventStore(GrainFactory);
        var agentId = Guid.NewGuid();

        // Act
        var snapshot = await eventStore.GetLatestSnapshotAsync(agentId);

        // Assert
        Assert.Null(snapshot);
    }
}

