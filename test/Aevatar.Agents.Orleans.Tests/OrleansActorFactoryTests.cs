using System;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Orleans;
using Aevatar.Agents.TestBase;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Xunit;

namespace Aevatar.Agents.Orleans.Tests;

/// <summary>
/// Tests for Orleans GAgent Actor Factory
/// Focuses on factory functionality and Orleans integration
/// </summary>
public class OrleansActorFactoryTests : AevatarAgentsTestBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IGrainFactory _grainFactory;
    
    public OrleansActorFactoryTests(ClusterFixture fixture) : base(fixture)
    {
        _serviceProvider = ServiceProvider;
        _grainFactory = GrainFactory;
    }
    
    [Fact]
    public async Task Factory_Should_Create_Actor_With_Orleans_Grain()
    {
        // Arrange
        var logger = _serviceProvider.GetRequiredService<ILogger<OrleansGAgentActorFactory>>();
        var options = _serviceProvider.GetService<IOptions<OrleansGAgentActorFactoryOptions>>();
        
        var factory = new OrleansGAgentActorFactory(
            _serviceProvider,
            _grainFactory,
            logger,
            options);
        
        var agentId = Guid.NewGuid();
        
        // Act
        var actor = await factory.CreateAgentAsync<OrleansTestAgent, OrleansTestState>(agentId);
        
        // Assert
        Assert.NotNull(actor);
        Assert.IsAssignableFrom<IGAgentActor>(actor);
        Assert.IsType<OrleansGAgentActor>(actor);
        Assert.Equal(agentId, actor.Id);
    }
    
    [Fact]
    public async Task Factory_Should_Create_Actor_With_Single_Generic_Parameter()
    {
        // Arrange
        var logger = _serviceProvider.GetRequiredService<ILogger<OrleansGAgentActorFactory>>();
        var options = _serviceProvider.GetService<IOptions<OrleansGAgentActorFactoryOptions>>();
        
        var factory = new OrleansGAgentActorFactory(
            _serviceProvider,
            _grainFactory,
            logger,
            options);
        
        var agentId = Guid.NewGuid();
        
        // Act
        var actor = await factory.CreateAgentAsync<OrleansTestAgent>(agentId);
        
        // Assert
        Assert.NotNull(actor);
        Assert.IsAssignableFrom<IGAgentActor>(actor);
        Assert.Equal(agentId, actor.Id);
        
        // Verify agent is properly initialized
        var agent = actor.GetAgent();
        Assert.NotNull(agent);
        Assert.IsType<OrleansTestAgent>(agent);
    }
    
    [Fact]
    public async Task Factory_Created_Actors_Should_Support_Hierarchical_Relationships()
    {
        // Arrange
        var logger = _serviceProvider.GetRequiredService<ILogger<OrleansGAgentActorFactory>>();
        var options = _serviceProvider.GetService<IOptions<OrleansGAgentActorFactoryOptions>>();
        
        var factory = new OrleansGAgentActorFactory(
            _serviceProvider,
            _grainFactory,
            logger,
            options);
        
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        
        // Act
        var parent = await factory.CreateAgentAsync<OrleansTestAgent>(parentId);
        var child = await factory.CreateAgentAsync<OrleansTestAgent>(childId);
        
        await parent.AddChildAsync(childId);
        await child.SetParentAsync(parentId);
        
        // Assert
        var childrenIds = await parent.GetChildrenAsync();
        var parentFromChild = await child.GetParentAsync();
        
        Assert.Contains(childId, childrenIds);
        Assert.Equal(parentId, parentFromChild);
    }
    
    [Fact]
    public async Task Factory_Created_Actors_Should_Support_Event_Publishing()
    {
        // Arrange
        var logger = _serviceProvider.GetRequiredService<ILogger<OrleansGAgentActorFactory>>();
        var options = _serviceProvider.GetService<IOptions<OrleansGAgentActorFactoryOptions>>();
        
        var factory = new OrleansGAgentActorFactory(
            _serviceProvider,
            _grainFactory,
            logger,
            options);
        
        var agentId = Guid.NewGuid();
        var actor = await factory.CreateAgentAsync<OrleansTestAgent>(agentId);
        
        // Act - Publish events with different directions (should not throw)
        var testEvent = new StringValue { Value = "test message" };
        
        var eventId1 = await actor.PublishEventAsync(testEvent, EventDirection.Down);
        var eventId2 = await actor.PublishEventAsync(testEvent, EventDirection.Up);
        var eventId3 = await actor.PublishEventAsync(testEvent, EventDirection.Bidirectional);
        
        // Assert
        Assert.NotNull(eventId1);
        Assert.NotNull(eventId2);
        Assert.NotNull(eventId3);
        Assert.NotEqual(eventId1, eventId2);
        Assert.NotEqual(eventId2, eventId3);
    }
    
    [Fact]
    public async Task Multiple_Actors_Should_Work_Independently()
    {
        // Arrange
        var logger = _serviceProvider.GetRequiredService<ILogger<OrleansGAgentActorFactory>>();
        var options = _serviceProvider.GetService<IOptions<OrleansGAgentActorFactoryOptions>>();
        
        var factory = new OrleansGAgentActorFactory(
            _serviceProvider,
            _grainFactory,
            logger,
            options);
        
        var agentId1 = Guid.NewGuid();
        var agentId2 = Guid.NewGuid();
        
        // Act
        var actor1 = await factory.CreateAgentAsync<OrleansTestAgent>(agentId1);
        var actor2 = await factory.CreateAgentAsync<OrleansTestAgent>(agentId2);
        
        // Add different children to each
        var child1 = Guid.NewGuid();
        var child2 = Guid.NewGuid();
        var child3 = Guid.NewGuid();
        
        await actor1.AddChildAsync(child1);
        await actor2.AddChildAsync(child2);
        await actor2.AddChildAsync(child3);
        
        // Assert - Each actor maintains its own state
        var children1 = await actor1.GetChildrenAsync();
        var children2 = await actor2.GetChildrenAsync();
        
        Assert.Single(children1);
        Assert.Contains(child1, children1);
        
        Assert.Equal(2, children2.Count);
        Assert.Contains(child2, children2);
        Assert.Contains(child3, children2);
    }
    
    [Fact]
    public async Task Factory_Should_Handle_Concurrent_Creation()
    {
        // Arrange
        var logger = _serviceProvider.GetRequiredService<ILogger<OrleansGAgentActorFactory>>();
        var options = _serviceProvider.GetService<IOptions<OrleansGAgentActorFactoryOptions>>();
        
        var factory = new OrleansGAgentActorFactory(
            _serviceProvider,
            _grainFactory,
            logger,
            options);
        
        // Act - Create multiple actors concurrently
        var tasks = new Task<IGAgentActor>[5];
        for (int i = 0; i < 5; i++)
        {
            var id = Guid.NewGuid();
            tasks[i] = factory.CreateAgentAsync<OrleansTestAgent>(id);
        }
        
        var actors = await Task.WhenAll(tasks);
        
        // Assert - All actors should be created successfully
        Assert.Equal(5, actors.Length);
        foreach (var actor in actors)
        {
            Assert.NotNull(actor);
            Assert.IsType<OrleansGAgentActor>(actor);
        }
        
        // Verify all actors have unique IDs
        var ids = new HashSet<Guid>();
        foreach (var actor in actors)
        {
            Assert.True(ids.Add(actor.Id), $"Duplicate actor ID found: {actor.Id}");
        }
    }
}

// Test Agent Implementation for Orleans
public class OrleansTestAgent : IGAgent<OrleansTestState>
{
    public Guid Id { get; set; }
    public OrleansTestState State { get; set; } = new();
    public IEventPublisher? EventPublisher { get; set; }
    
    // Default constructor
    public OrleansTestAgent()
    {
    }
    
    // Constructor with ID parameter for dependency injection
    public OrleansTestAgent(Guid id)
    {
        Id = id;
    }

    public Task InitializeAsync()
    {
        State.IsInitialized = true;
        return Task.CompletedTask;
    }
    
    public Task<string> ProcessAsync(IMessage message)
    {
        State.ProcessedCount++;
        return Task.FromResult($"Processed by Orleans agent {Id}");
    }
    
    public OrleansTestState GetState()
    {
        return State;
    }
    
    public Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"OrleansTestAgent {Id}");
    }
    
    [EventHandler]
    public Task HandleTestEvent(StringValue message)
    {
        State.LastMessage = message.Value;
        State.EventCount++;
        return Task.CompletedTask;
    }
    
    public void Dispose()
    {
        State.IsDisposed = true;
    }
}

public class OrleansTestState
{
    public bool IsInitialized { get; set; }
    public bool IsDisposed { get; set; }
    public int ProcessedCount { get; set; }
    public int EventCount { get; set; }
    public string LastMessage { get; set; } = "";
}
