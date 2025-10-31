using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Core.EventSourcing;
using Xunit;

namespace Aevatar.Agents.Core.Tests.EventSourcing;

public class InMemoryEventStoreTests
{
    [Fact]
    public async Task SaveEventAsync_Should_StoreEvent()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var agentId = Guid.NewGuid();
        var logEvent = CreateTestLogEvent(agentId, 1);
        
        // Act
        await store.SaveEventAsync(agentId, logEvent);
        
        // Assert
        var events = await store.GetEventsAsync(agentId);
        Assert.Single(events);
        Assert.Equal(logEvent.EventId, events[0].EventId);
        Assert.Equal(logEvent.Version, events[0].Version);
    }
    
    [Fact]
    public async Task SaveEventsAsync_Should_StoreMultipleEvents()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var agentId = Guid.NewGuid();
        var events = new List<StateLogEvent>
        {
            CreateTestLogEvent(agentId, 1),
            CreateTestLogEvent(agentId, 2),
            CreateTestLogEvent(agentId, 3)
        };
        
        // Act
        await store.SaveEventsAsync(agentId, events);
        
        // Assert
        var storedEvents = await store.GetEventsAsync(agentId);
        Assert.Equal(3, storedEvents.Count);
        Assert.Equal(1, storedEvents[0].Version);
        Assert.Equal(2, storedEvents[1].Version);
        Assert.Equal(3, storedEvents[2].Version);
    }
    
    [Fact]
    public async Task GetEventsAsync_Should_ReturnEventsInOrder()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var agentId = Guid.NewGuid();
        
        // Add events out of order
        await store.SaveEventAsync(agentId, CreateTestLogEvent(agentId, 3));
        await store.SaveEventAsync(agentId, CreateTestLogEvent(agentId, 1));
        await store.SaveEventAsync(agentId, CreateTestLogEvent(agentId, 2));
        
        // Act
        var events = await store.GetEventsAsync(agentId);
        
        // Assert
        Assert.Equal(3, events.Count);
        Assert.Equal(1, events[0].Version);
        Assert.Equal(2, events[1].Version);
        Assert.Equal(3, events[2].Version);
    }
    
    [Fact]
    public async Task GetEventsAsync_WithVersionRange_Should_FilterCorrectly()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var agentId = Guid.NewGuid();
        
        for (int i = 1; i <= 10; i++)
        {
            await store.SaveEventAsync(agentId, CreateTestLogEvent(agentId, i));
        }
        
        // Act
        var events = await store.GetEventsAsync(agentId, fromVersion: 3, toVersion: 7);
        
        // Assert
        Assert.Equal(5, events.Count); // versions 3, 4, 5, 6, 7
        Assert.Equal(3, events[0].Version);
        Assert.Equal(7, events[^1].Version);
    }
    
    [Fact]
    public async Task GetLatestVersionAsync_Should_ReturnCorrectVersion()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var agentId = Guid.NewGuid();
        
        // Act & Assert - No events
        var version = await store.GetLatestVersionAsync(agentId);
        Assert.Equal(0, version);
        
        // Add events
        await store.SaveEventAsync(agentId, CreateTestLogEvent(agentId, 1));
        await store.SaveEventAsync(agentId, CreateTestLogEvent(agentId, 5));
        await store.SaveEventAsync(agentId, CreateTestLogEvent(agentId, 3));
        
        // Act & Assert - With events
        version = await store.GetLatestVersionAsync(agentId);
        Assert.Equal(5, version);
    }
    
    [Fact]
    public async Task ClearEventsAsync_Should_RemoveAllEvents()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var agentId = Guid.NewGuid();
        
        await store.SaveEventAsync(agentId, CreateTestLogEvent(agentId, 1));
        await store.SaveEventAsync(agentId, CreateTestLogEvent(agentId, 2));
        
        // Act
        await store.ClearEventsAsync(agentId);
        
        // Assert
        var events = await store.GetEventsAsync(agentId);
        Assert.Empty(events);
        
        var version = await store.GetLatestVersionAsync(agentId);
        Assert.Equal(0, version);
    }
    
    [Fact]
    public async Task GetEventsAsync_ForNonExistentAgent_Should_ReturnEmpty()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var agentId = Guid.NewGuid();
        
        // Act
        var events = await store.GetEventsAsync(agentId);
        
        // Assert
        Assert.Empty(events);
    }
    
    [Fact]
    public async Task MultipleAgents_Should_HaveIsolatedEvents()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var agentId1 = Guid.NewGuid();
        var agentId2 = Guid.NewGuid();
        
        // Act
        await store.SaveEventAsync(agentId1, CreateTestLogEvent(agentId1, 1));
        await store.SaveEventAsync(agentId1, CreateTestLogEvent(agentId1, 2));
        await store.SaveEventAsync(agentId2, CreateTestLogEvent(agentId2, 1));
        
        // Assert
        var events1 = await store.GetEventsAsync(agentId1);
        var events2 = await store.GetEventsAsync(agentId2);
        
        Assert.Equal(2, events1.Count);
        Assert.Equal(1, events2.Count);
        
        Assert.All(events1, e => Assert.Equal(agentId1, e.AgentId));
        Assert.All(events2, e => Assert.Equal(agentId2, e.AgentId));
    }
    
    [Fact]
    public async Task ConcurrentWrites_Should_BeThreadSafe()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var agentId = Guid.NewGuid();
        var tasks = new List<Task>();
        
        // Act - Write 100 events concurrently
        for (int i = 1; i <= 100; i++)
        {
            var version = i;
            tasks.Add(Task.Run(async () =>
            {
                await store.SaveEventAsync(agentId, CreateTestLogEvent(agentId, version));
            }));
        }
        
        await Task.WhenAll(tasks);
        
        // Assert
        var events = await store.GetEventsAsync(agentId);
        Assert.Equal(100, events.Count);
        
        // Check all versions are present
        var versions = events.Select(e => e.Version).OrderBy(v => v).ToList();
        for (int i = 1; i <= 100; i++)
        {
            Assert.Contains(i, versions);
        }
    }
    
    private StateLogEvent CreateTestLogEvent(Guid agentId, long version)
    {
        return new StateLogEvent
        {
            EventId = Guid.NewGuid(),
            AgentId = agentId,
            Version = version,
            EventType = "TestEvent",
            EventData = new byte[] { 1, 2, 3 },
            TimestampUtc = DateTime.UtcNow,
            Metadata = "test=value"
        };
    }
}
