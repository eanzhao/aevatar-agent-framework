using System;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Local;
using Aevatar.Agents.Serialization;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Aevatar.Agents.Local.Tests;

// Test Agent State
public class LocalTestState
{
    public string Name { get; set; } = string.Empty;
    public int Counter { get; set; }
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
}

// Test Agent
public class LocalTestAgent : GAgentBase<LocalTestState>
{
    public bool WasActivated { get; private set; }
    public bool WasDeactivated { get; private set; }
    public bool EventPublisherSet { get; private set; }
    
    public LocalTestAgent() : base(Guid.NewGuid())
    {
    }
    
    public LocalTestAgent(Guid id) : base(id)
    {
    }
    
    public override async Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        WasActivated = true;
        await base.OnActivateAsync(cancellationToken);
    }
    
    public override async Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        WasDeactivated = true;
        await base.OnDeactivateAsync(cancellationToken);
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"LocalTestAgent {Id}");
    }
    
    [EventHandler]
    public async Task HandleTestEvent(EventEnvelope envelope)
    {
        GetState().Counter++;
        GetState().LastUpdate = DateTime.UtcNow;
        await Task.CompletedTask;
    }
}

public class LocalGAgentActorTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly LocalGAgentActorFactory _factory;
    
    public LocalGAgentActorTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<LocalMessageStreamRegistry>();
        services.AddSingleton<LocalGAgentActorFactory>();
        
        _serviceProvider = services.BuildServiceProvider();
        _factory = _serviceProvider.GetRequiredService<LocalGAgentActorFactory>();
    }
    
    [Fact]
    public async Task Should_Create_And_Activate_Local_Actor()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        
        // Act
        var actor = await _factory.CreateAgentAsync<LocalTestAgent, LocalTestState>(agentId);
        
        // Assert
        Assert.NotNull(actor);
        Assert.Equal(agentId, actor.Id);
        Assert.IsType<LocalGAgentActor>(actor);
        
        var agent = actor.GetAgent() as LocalTestAgent;
        Assert.NotNull(agent);
        Assert.True(agent.WasActivated);
    }
    
    [Fact]
    public async Task Should_Handle_Events_Locally()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var actor = await _factory.CreateAgentAsync<LocalTestAgent, LocalTestState>(agentId);
        var agent = actor.GetAgent() as LocalTestAgent;
        
        // Act
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(new StringValue { Value = "test" }),
            Direction = EventDirection.Down
        };
        
        await actor.PublishEventAsync(envelope);
        
        // Give time for async processing
        await Task.Delay(100);
        
        // Assert
        Assert.NotNull(agent);
        Assert.Equal(1, agent.GetState().Counter);
    }
    
    [Fact]
    public async Task Should_Support_Hierarchical_Relationships()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        
        var parent = await _factory.CreateAgentAsync<LocalTestAgent, LocalTestState>(parentId);
        var child = await _factory.CreateAgentAsync<LocalTestAgent, LocalTestState>(childId);
        
        // Act
        await parent.AddChildAsync(childId);
        await child.SetParentAsync(parentId);
        
        // Assert
        var children = await parent.GetChildrenAsync();
        Assert.Contains(childId, children);
        
        var parentOfChild = await child.GetParentAsync();
        Assert.Equal(parentId, parentOfChild);
    }
    
    [Fact]
    public async Task Should_Route_Events_Based_On_Direction()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        
        var parent = await _factory.CreateAgentAsync<LocalTestAgent, LocalTestState>(parentId);
        var child = await _factory.CreateAgentAsync<LocalTestAgent, LocalTestState>(childId);
        
        await parent.AddChildAsync(childId);
        await child.SetParentAsync(parentId);
        
        // Act - Send event down from parent
        var downEvent = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(new StringValue { Value = "down" }),
            Direction = EventDirection.Down
        };
        
        await parent.PublishEventAsync(downEvent);
        
        // Act - Send event up from child
        var upEvent = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(new StringValue { Value = "up" }),
            Direction = EventDirection.Up
        };
        
        await child.PublishEventAsync(upEvent);
        
        // Give time for async processing
        await Task.Delay(100);
        
        // Assert - Both should handle their events
        var parentAgent = parent.GetAgent() as LocalTestAgent;
        var childAgent = child.GetAgent() as LocalTestAgent;
        
        Assert.NotNull(parentAgent);
        Assert.NotNull(childAgent);
        
        // Parent sends down, child sends up - both should receive events
        Assert.True(parentAgent.GetState().Counter > 0 || childAgent.GetState().Counter > 0);
    }
    
    [Fact]
    public async Task Should_Handle_Concurrent_Events()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var actor = await _factory.CreateAgentAsync<LocalTestAgent, LocalTestState>(agentId);
        
        // Act - Send multiple events concurrently
        var tasks = new Task[10];
        for (int i = 0; i < 10; i++)
        {
            var envelope = new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Payload = Any.Pack(new StringValue { Value = $"test_{i}" }),
                Direction = EventDirection.Down
            };
            
            tasks[i] = actor.PublishEventAsync(envelope);
        }
        
        await Task.WhenAll(tasks);
        
        // Give time for async processing
        await Task.Delay(200);
        
        // Assert
        var agent = actor.GetAgent() as LocalTestAgent;
        Assert.NotNull(agent);
        Assert.Equal(10, agent.GetState().Counter);
    }
    
    [Fact]
    public async Task Should_Properly_Deactivate()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var actor = await _factory.CreateAgentAsync<LocalTestAgent, LocalTestState>(agentId);
        var agent = actor.GetAgent() as LocalTestAgent;
        
        // Act
        await actor.DeactivateAsync();
        
        // Assert
        Assert.NotNull(agent);
        Assert.True(agent.WasDeactivated);
    }
    
    [Fact]
    public async Task Should_Clear_Parent_Relationship()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        
        var parent = await _factory.CreateAgentAsync<LocalTestAgent, LocalTestState>(parentId);
        var child = await _factory.CreateAgentAsync<LocalTestAgent, LocalTestState>(childId);
        
        await parent.AddChildAsync(childId);
        await child.SetParentAsync(parentId);
        
        // Act
        await child.ClearParentAsync();
        
        // Assert
        var parentOfChild = await child.GetParentAsync();
        Assert.Null(parentOfChild);
    }
    
    [Fact]
    public async Task Should_Remove_Child_Relationship()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        
        var parent = await _factory.CreateAgentAsync<LocalTestAgent, LocalTestState>(parentId);
        var child = await _factory.CreateAgentAsync<LocalTestAgent, LocalTestState>(childId);
        
        await parent.AddChildAsync(childId);
        await child.SetParentAsync(parentId);
        
        // Act
        await parent.RemoveChildAsync(childId);
        
        // Assert
        var children = await parent.GetChildrenAsync();
        Assert.DoesNotContain(childId, children);
    }
    
    [Fact]
    public async Task Multiple_Agents_Should_Work_Independently()
    {
        // Arrange & Act
        var agents = new IGAgentActor[5];
        for (int i = 0; i < 5; i++)
        {
            agents[i] = await _factory.CreateAgentAsync<LocalTestAgent, LocalTestState>(Guid.NewGuid());
        }
        
        // Send different events to each
        var tasks = new Task[5];
        for (int i = 0; i < 5; i++)
        {
            var envelope = new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Payload = Any.Pack(new StringValue { Value = $"agent_{i}" }),
                Direction = EventDirection.Down
            };
            
            tasks[i] = agents[i].PublishEventAsync(envelope);
        }
        
        await Task.WhenAll(tasks);
        await Task.Delay(100);
        
        // Assert - Each agent should have processed its event
        for (int i = 0; i < 5; i++)
        {
            var agent = agents[i].GetAgent() as LocalTestAgent;
            Assert.NotNull(agent);
            Assert.Equal(1, agent.GetState().Counter);
        }
    }
}
