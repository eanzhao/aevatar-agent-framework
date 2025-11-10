using System;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Runtime.ProtoActor;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Proto;
using Proto.Remote;
using Proto.Remote.GrpcNet;
using Xunit;

namespace Aevatar.Agents.ProtoActor.Tests;

// Test Agent State
public class ProtoTestState
{
    public string Name { get; set; } = string.Empty;
    public int Counter { get; set; }
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
}

// Test Agent
public class ProtoTestAgent : GAgentBase<ProtoTestState>
{
    public bool WasActivated { get; private set; }
    public bool WasDeactivated { get; private set; }
    
    public ProtoTestAgent() : base(Guid.NewGuid())
    {
    }
    
    public ProtoTestAgent(Guid id) : base(id)
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
        return Task.FromResult($"ProtoTestAgent {Id}");
    }
    
    // Use StringValue as the test event type since it's a real IMessage implementation
    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleTestEvent(StringValue message)
    {
        Console.WriteLine($"ProtoTestAgent {Id} received message: {message.Value}");
        GetState().Counter++;
        GetState().LastUpdate = DateTime.UtcNow;
        await Task.CompletedTask;
    }
    
    // Public methods for testing event publishing
    public async Task SendEventUp(string message)
    {
        await PublishAsync(new StringValue { Value = message }, EventDirection.Up);
    }
    
    public async Task SendEventDown(string message)
    {
        await PublishAsync(new StringValue { Value = message }, EventDirection.Down);
    }
    
    public async Task SendEventBoth(string message)
    {
        await PublishAsync(new StringValue { Value = message }, EventDirection.Both);
    }
}

public class ProtoActorGAgentActorTests : IDisposable
{
    private readonly ActorSystem _actorSystem;
    private readonly ProtoActorGAgentActorFactory _factory;
    private readonly IServiceProvider _serviceProvider;
    
    public ProtoActorGAgentActorTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        
        // Setup ProtoActor
        var systemConfig = ActorSystemConfig.Setup();
        _actorSystem = new ActorSystem(systemConfig);
        
        services.AddSingleton(_actorSystem);
        services.AddSingleton<ProtoActorMessageStreamRegistry>();
        services.AddSingleton<ProtoActorGAgentActorFactory>();
        services.AddGAgentActorFactoryProvider();  // 添加工厂提供者
        
        _serviceProvider = services.BuildServiceProvider();
        _factory = _serviceProvider.GetRequiredService<ProtoActorGAgentActorFactory>();
    }
    
    public void Dispose()
    {
        // ActorSystem doesn't have Dispose in current version
        // Shutdown is handled automatically
    }
    
    [Fact]
    public async Task Should_Create_And_Activate_ProtoActor()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        
        // Act
        var actor = await _factory.CreateGAgentActorAsync<ProtoTestAgent>(agentId);
        
        // Assert
        Assert.NotNull(actor);
        Assert.Equal(agentId, actor.Id);
        Assert.IsType<ProtoActorGAgentActor>(actor);
        
        var agent = actor.GetAgent() as ProtoTestAgent;
        Assert.NotNull(agent);
        Assert.True(agent.WasActivated);
    }
    
    [Fact]
    public async Task Should_Handle_Events_Through_ProtoActor()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var actor = await _factory.CreateGAgentActorAsync<ProtoTestAgent>(agentId);
        var agent = actor.GetAgent() as ProtoTestAgent;
        
        // Act
        var message = new StringValue { Value = "test" };
        await actor.PublishEventAsync(message, EventDirection.Down);
        
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
        
        var parent = await _factory.CreateGAgentActorAsync<ProtoTestAgent>(parentId);
        var child = await _factory.CreateGAgentActorAsync<ProtoTestAgent>(childId);
        
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
        
        var parent = await _factory.CreateGAgentActorAsync<ProtoTestAgent>(parentId);
        var child = await _factory.CreateGAgentActorAsync<ProtoTestAgent>(childId);
        
        await parent.AddChildAsync(childId);
        await child.SetParentAsync(parentId);
        
        // Get the agents
        var parentAgent = parent.GetAgent() as ProtoTestAgent;
        var childAgent = child.GetAgent() as ProtoTestAgent;
        
        Assert.NotNull(parentAgent);
        Assert.NotNull(childAgent);
        
        // Reset counters
        parentAgent.GetState().Counter = 0;
        childAgent.GetState().Counter = 0;
        
        // Act - Parent sends event down to child
        await parentAgent.SendEventDown("down");
        
        // Act - Child sends event up to parent
        await childAgent.SendEventUp("up");
        
        // Give time for async processing
        await Task.Delay(200);
        
        // Assert - Check that events were routed correctly
        // Parent should have received the UP event from child
        // Child should have received the DOWN event from parent
        Assert.True(parentAgent.GetState().Counter > 0, "Parent should have received UP event from child");
        Assert.True(childAgent.GetState().Counter > 0, "Child should have received DOWN event from parent");
    }
    
    [Fact]
    public async Task Should_Handle_Concurrent_Events()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var actor = await _factory.CreateGAgentActorAsync<ProtoTestAgent>(agentId);
        
        // Act - Send multiple events concurrently
        var tasks = new Task[10];
        for (int i = 0; i < 10; i++)
        {
            var message = new StringValue { Value = $"test_{i}" };
            tasks[i] = actor.PublishEventAsync(message, EventDirection.Down);
        }
        
        await Task.WhenAll(tasks);
        
        // Give time for async processing
        await Task.Delay(200);
        
        // Assert
        var agent = actor.GetAgent() as ProtoTestAgent;
        Assert.NotNull(agent);
        Assert.Equal(10, agent.GetState().Counter);
    }
    
    [Fact]
    public async Task Should_Properly_Deactivate()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var actor = await _factory.CreateGAgentActorAsync<ProtoTestAgent>(agentId);
        var agent = actor.GetAgent() as ProtoTestAgent;
        
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
        
        var parent = await _factory.CreateGAgentActorAsync<ProtoTestAgent>(parentId);
        var child = await _factory.CreateGAgentActorAsync<ProtoTestAgent>(childId);
        
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
        
        var parent = await _factory.CreateGAgentActorAsync<ProtoTestAgent>(parentId);
        var child = await _factory.CreateGAgentActorAsync<ProtoTestAgent>(childId);
        
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
            agents[i] = await _factory.CreateGAgentActorAsync<ProtoTestAgent>(Guid.NewGuid());
        }
        
        // Send different events to each
        var tasks = new Task[5];
        for (int i = 0; i < 5; i++)
        {
            var message = new StringValue { Value = $"agent_{i}" };
            tasks[i] = agents[i].PublishEventAsync(message, EventDirection.Down);
        }
        
        await Task.WhenAll(tasks);
        await Task.Delay(100);
        
        // Assert - Each agent should have processed its event
        for (int i = 0; i < 5; i++)
        {
            var agent = agents[i].GetAgent() as ProtoTestAgent;
            Assert.NotNull(agent);
            Assert.Equal(1, agent.GetState().Counter);
        }
    }
    
    [Fact]
    public async Task Should_Handle_ProtoActor_Specific_Features()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var actor = await _factory.CreateGAgentActorAsync<ProtoTestAgent>(agentId);
        
        // Act - Test ProtoActor-specific message handling
        var protoActor = actor as ProtoActorGAgentActor;
        Assert.NotNull(protoActor);
        
        // ProtoActor specific verification
        // Note: PID is internal to the actor
        
        // Send a message directly through ProtoActor
        var message = new StringValue { Value = "direct" };
        await actor.PublishEventAsync(message, EventDirection.Down);
        await Task.Delay(100);
        
        // Assert
        var agent = actor.GetAgent() as ProtoTestAgent;
        Assert.NotNull(agent);
        Assert.True(agent.GetState().Counter > 0);
    }
}
