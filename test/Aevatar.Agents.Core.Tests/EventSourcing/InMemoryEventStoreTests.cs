using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Core.EventSourcing;
using FluentAssertions;
using Xunit;

namespace Aevatar.Agents.Core.Tests.EventSourcing;

public class InMemoryEventStoreTests
{
    [Fact(DisplayName = "SaveEventAsync should save single event")]
    public async Task SaveEventAsync_ShouldSaveSingleEvent()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var agentId = Guid.NewGuid();
        var logEvent = new StateLogEvent
        {
            AgentId = agentId,
            Version = 1,
            EventType = "TestEvent",
            EventData = new byte[] { 1, 2, 3 },
            Metadata = "test-metadata"
        };
        
        // Act
        await store.SaveEventAsync(agentId, logEvent);
        var events = await store.GetEventsAsync(agentId);
        
        // Assert
        events.Should().HaveCount(1);
        events[0].Version.Should().Be(1);
        events[0].EventType.Should().Be("TestEvent");
        events[0].EventData.Should().BeEquivalentTo(new byte[] { 1, 2, 3 });
        events[0].Metadata.Should().Be("test-metadata");
    }
    
    [Fact(DisplayName = "SaveEventsAsync should save multiple events")]
    public async Task SaveEventsAsync_ShouldSaveMultipleEvents()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var agentId = Guid.NewGuid();
        var logEvents = new[]
        {
            new StateLogEvent { AgentId = agentId, Version = 1, EventType = "Event1" },
            new StateLogEvent { AgentId = agentId, Version = 2, EventType = "Event2" },
            new StateLogEvent { AgentId = agentId, Version = 3, EventType = "Event3" }
        };
        
        // Act
        await store.SaveEventsAsync(agentId, logEvents);
        var events = await store.GetEventsAsync(agentId);
        
        // Assert
        events.Should().HaveCount(3);
        events.Select(e => e.EventType).Should().BeEquivalentTo("Event1", "Event2", "Event3");
        events.Select(e => e.Version).Should().BeInAscendingOrder();
    }
    
    [Fact(DisplayName = "GetEventsAsync should return empty list for unknown agent")]
    public async Task GetEventsAsync_ShouldReturnEmptyListForUnknownAgent()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var unknownAgentId = Guid.NewGuid();
        
        // Act
        var events = await store.GetEventsAsync(unknownAgentId);
        
        // Assert
        events.Should().NotBeNull();
        events.Should().BeEmpty();
    }
    
    [Fact(DisplayName = "GetEventsAsync should return events sorted by version")]
    public async Task GetEventsAsync_ShouldReturnEventsSortedByVersion()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var agentId = Guid.NewGuid();
        
        // Save events in random order
        await store.SaveEventAsync(agentId, new StateLogEvent { AgentId = agentId, Version = 3, EventType = "Event3" });
        await store.SaveEventAsync(agentId, new StateLogEvent { AgentId = agentId, Version = 1, EventType = "Event1" });
        await store.SaveEventAsync(agentId, new StateLogEvent { AgentId = agentId, Version = 2, EventType = "Event2" });
        
        // Act
        var events = await store.GetEventsAsync(agentId);
        
        // Assert
        events.Should().HaveCount(3);
        events[0].Version.Should().Be(1);
        events[1].Version.Should().Be(2);
        events[2].Version.Should().Be(3);
    }
    
    [Fact(DisplayName = "GetEventsAsync with version range should filter correctly")]
    public async Task GetEventsAsync_WithVersionRange_ShouldFilterCorrectly()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var agentId = Guid.NewGuid();
        
        for (int i = 1; i <= 10; i++)
        {
            await store.SaveEventAsync(agentId, new StateLogEvent 
            { 
                AgentId = agentId, 
                Version = i, 
                EventType = $"Event{i}" 
            });
        }
        
        // Act
        var events = await store.GetEventsAsync(agentId, 3, 7);
        
        // Assert
        events.Should().HaveCount(5);
        events.Select(e => e.Version).Should().BeEquivalentTo(new[] { 3, 4, 5, 6, 7 });
        events.Select(e => e.EventType).Should().BeEquivalentTo("Event3", "Event4", "Event5", "Event6", "Event7");
    }
    
    [Fact(DisplayName = "GetEventsAsync with version range should return empty for no matches")]
    public async Task GetEventsAsync_WithVersionRange_ShouldReturnEmptyForNoMatches()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var agentId = Guid.NewGuid();
        
        await store.SaveEventAsync(agentId, new StateLogEvent { AgentId = agentId, Version = 1 });
        await store.SaveEventAsync(agentId, new StateLogEvent { AgentId = agentId, Version = 2 });
        
        // Act
        var events = await store.GetEventsAsync(agentId, 5, 10);
        
        // Assert
        events.Should().BeEmpty();
    }
    
    [Fact(DisplayName = "GetLatestVersionAsync should return latest version")]
    public async Task GetLatestVersionAsync_ShouldReturnLatestVersion()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var agentId = Guid.NewGuid();
        
        await store.SaveEventAsync(agentId, new StateLogEvent { AgentId = agentId, Version = 5 });
        await store.SaveEventAsync(agentId, new StateLogEvent { AgentId = agentId, Version = 10 });
        await store.SaveEventAsync(agentId, new StateLogEvent { AgentId = agentId, Version = 7 });
        
        // Act
        var latestVersion = await store.GetLatestVersionAsync(agentId);
        
        // Assert
        latestVersion.Should().Be(10);
    }
    
    [Fact(DisplayName = "GetLatestVersionAsync should return 0 for unknown agent")]
    public async Task GetLatestVersionAsync_ShouldReturn0ForUnknownAgent()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var unknownAgentId = Guid.NewGuid();
        
        // Act
        var latestVersion = await store.GetLatestVersionAsync(unknownAgentId);
        
        // Assert
        latestVersion.Should().Be(0);
    }
    
    [Fact(DisplayName = "ClearEventsAsync should remove all events for agent")]
    public async Task ClearEventsAsync_ShouldRemoveAllEventsForAgent()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var agentId = Guid.NewGuid();
        
        await store.SaveEventsAsync(agentId, new[]
        {
            new StateLogEvent { AgentId = agentId, Version = 1 },
            new StateLogEvent { AgentId = agentId, Version = 2 }
        });
        
        // Act
        await store.ClearEventsAsync(agentId);
        var events = await store.GetEventsAsync(agentId);
        var latestVersion = await store.GetLatestVersionAsync(agentId);
        
        // Assert
        events.Should().BeEmpty();
        latestVersion.Should().Be(0);
    }
    
    [Fact(DisplayName = "Store should handle multiple agents independently")]
    public async Task Store_ShouldHandleMultipleAgentsIndependently()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var agentId1 = Guid.NewGuid();
        var agentId2 = Guid.NewGuid();
        
        // Act
        await store.SaveEventAsync(agentId1, new StateLogEvent { AgentId = agentId1, Version = 1, EventType = "Agent1Event" });
        await store.SaveEventAsync(agentId2, new StateLogEvent { AgentId = agentId2, Version = 1, EventType = "Agent2Event" });
        
        var events1 = await store.GetEventsAsync(agentId1);
        var events2 = await store.GetEventsAsync(agentId2);
        
        // Assert
        events1.Should().HaveCount(1);
        events1[0].EventType.Should().Be("Agent1Event");
        
        events2.Should().HaveCount(1);
        events2[0].EventType.Should().Be("Agent2Event");
    }
    
    [Fact(DisplayName = "Store should be thread-safe for concurrent operations")]
    public async Task Store_ShouldBeThreadSafeForConcurrentOperations()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var agentId = Guid.NewGuid();
        var tasks = new List<Task>();
        
        // Act - Save 100 events concurrently
        for (int i = 1; i <= 100; i++)
        {
            int version = i;
            tasks.Add(Task.Run(async () => 
            {
                await store.SaveEventAsync(agentId, new StateLogEvent 
                { 
                    AgentId = agentId, 
                    Version = version, 
                    EventType = $"Event{version}" 
                });
            }));
        }
        
        await Task.WhenAll(tasks);
        var events = await store.GetEventsAsync(agentId);
        
        // Assert
        events.Should().HaveCount(100);
        events.Select(e => e.Version).Should().BeEquivalentTo(Enumerable.Range(1, 100));
    }
}

public class StateLogEventTests
{
    [Fact(DisplayName = "StateLogEvent should initialize with default values")]
    public void StateLogEvent_ShouldInitializeWithDefaultValues()
    {
        // Arrange & Act
        var logEvent = new StateLogEvent();
        
        // Assert
        logEvent.EventId.Should().NotBe(Guid.Empty);
        logEvent.AgentId.Should().Be(Guid.Empty);
        logEvent.Version.Should().Be(0);
        logEvent.EventType.Should().BeEmpty();
        logEvent.EventData.Should().BeEmpty();
        logEvent.TimestampUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        logEvent.Metadata.Should().BeNull();
    }
    
    [Fact(DisplayName = "StateLogEvent should generate unique EventIds")]
    public void StateLogEvent_ShouldGenerateUniqueEventIds()
    {
        // Arrange & Act
        var event1 = new StateLogEvent();
        var event2 = new StateLogEvent();
        
        // Assert
        event1.EventId.Should().NotBe(event2.EventId);
    }
    
    [Fact(DisplayName = "StateLogEvent properties should be settable")]
    public void StateLogEvent_PropertiesShouldBeSettable()
    {
        // Arrange
        var logEvent = new StateLogEvent();
        var eventId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var version = 42L;
        var eventType = "TestEvent";
        var eventData = new byte[] { 1, 2, 3, 4, 5 };
        var timestamp = DateTime.UtcNow.AddMinutes(-5);
        var metadata = "test-metadata";
        
        // Act
        logEvent.EventId = eventId;
        logEvent.AgentId = agentId;
        logEvent.Version = version;
        logEvent.EventType = eventType;
        logEvent.EventData = eventData;
        logEvent.TimestampUtc = timestamp;
        logEvent.Metadata = metadata;
        
        // Assert
        logEvent.EventId.Should().Be(eventId);
        logEvent.AgentId.Should().Be(agentId);
        logEvent.Version.Should().Be(version);
        logEvent.EventType.Should().Be(eventType);
        logEvent.EventData.Should().BeEquivalentTo(eventData);
        logEvent.TimestampUtc.Should().Be(timestamp);
        logEvent.Metadata.Should().Be(metadata);
    }
}