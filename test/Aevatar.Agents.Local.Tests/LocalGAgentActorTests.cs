using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Aevatar.Agents.Local.Tests;

public class LocalGAgentActorTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly LocalGAgentActorFactory _factory;

    public LocalGAgentActorTests()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _serviceProvider = services.BuildServiceProvider();
        _factory = new LocalGAgentActorFactory(
            _serviceProvider, 
            _serviceProvider.GetRequiredService<ILogger<LocalGAgentActorFactory>>());
    }

    [Fact]
    public async Task CreateAgent_ShouldSucceed()
    {
        // Act
        var actor = await _factory.CreateAgentAsync<TestAgent, TestState>(Guid.NewGuid());
        
        // Assert
        Assert.NotNull(actor);
        Assert.NotEqual(Guid.Empty, actor.Id);
        
        // Cleanup
        await actor.DeactivateAsync();
    }

    [Fact]
    public async Task AddChild_ShouldEstablishHierarchy()
    {
        // Arrange
        var parentActor = await _factory.CreateAgentAsync<TestAgent, TestState>(Guid.NewGuid());
        var childActor = await _factory.CreateAgentAsync<TestAgent, TestState>(Guid.NewGuid());
        
        // Act
        await parentActor.AddChildAsync(childActor.Id);
        await childActor.SetParentAsync(parentActor.Id);
        
        // Assert
        var children = await parentActor.GetChildrenAsync();
        Assert.Contains(childActor.Id, children);
        
        var parent = await childActor.GetParentAsync();
        Assert.Equal(parentActor.Id, parent);
        
        // Cleanup
        await parentActor.DeactivateAsync();
        await childActor.DeactivateAsync();
    }

    [Fact]
    public async Task PublishEvent_WithDirectionDown_ShouldRouteToChildren()
    {
        // Arrange
        var parentActor = await _factory.CreateAgentAsync<TestAgent, TestState>(Guid.NewGuid());
        var childActor = await _factory.CreateAgentAsync<TestAgent, TestState>(Guid.NewGuid());
        
        await parentActor.AddChildAsync(childActor.Id);
        await childActor.SetParentAsync(parentActor.Id);
        
        // Act
        var testEvent = new GeneralConfigEvent { ConfigKey = "test", ConfigValue = "value" };
        await parentActor.PublishEventAsync(testEvent, EventDirection.Down);
        
        // 等待事件处理
        await Task.Delay(100);
        
        // Assert
        var childAgent = (TestAgent)childActor.GetAgent();
        var childState = childAgent.GetState();
        Assert.Equal("test", childState.Name);
        
        // Cleanup
        await parentActor.DeactivateAsync();
        await childActor.DeactivateAsync();
    }

    [Fact]
    public async Task PublishEvent_WithDirectionUp_ShouldRouteToParent()
    {
        // Arrange
        var parentActor = await _factory.CreateAgentAsync<TestAgent, TestState>(Guid.NewGuid());
        var childActor = await _factory.CreateAgentAsync<TestAgent, TestState>(Guid.NewGuid());
        
        await parentActor.AddChildAsync(childActor.Id);
        await childActor.SetParentAsync(parentActor.Id);
        
        // Act
        var testEvent = new GeneralConfigEvent { ConfigKey = "from-child", ConfigValue = "value" };
        await childActor.PublishEventAsync(testEvent, EventDirection.Up);
        
        // 等待事件处理
        await Task.Delay(100);
        
        // Assert
        var parentAgent = (TestAgent)parentActor.GetAgent();
        var parentState = parentAgent.GetState();
        Assert.Equal("from-child", parentState.Name);
        
        // Cleanup
        await parentActor.DeactivateAsync();
        await childActor.DeactivateAsync();
    }

    [Fact]
    public async Task PublishEvent_WithHopCountLimit_ShouldStopPropagation()
    {
        // Arrange
        var agent1 = await _factory.CreateAgentAsync<TestAgent, TestState>(Guid.NewGuid());
        var agent2 = await _factory.CreateAgentAsync<TestAgent, TestState>(Guid.NewGuid());
        var agent3 = await _factory.CreateAgentAsync<TestAgent, TestState>(Guid.NewGuid());
        
        // 建立链: agent1 -> agent2 -> agent3
        await agent1.AddChildAsync(agent2.Id);
        await agent2.SetParentAsync(agent1.Id);
        await agent2.AddChildAsync(agent3.Id);
        await agent3.SetParentAsync(agent2.Id);
        
        // Act - 发布事件，最大跳数为 1
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Version = 1,
            Payload = Any.Pack(new GeneralConfigEvent { ConfigKey = "hop-test", ConfigValue = "value" }),
            PublisherId = agent1.Id.ToString(),
            Direction = EventDirection.Down,
            MaxHopCount = 1,
            CurrentHopCount = 0
        };
        
        await agent1.HandleEventAsync(envelope);
        await Task.Delay(500);  // 增加等待时间，让 Stream 有时间处理

        // Assert
        var agent2State = ((TestAgent)agent2.GetAgent()).GetState();
        var agent3State = ((TestAgent)agent3.GetAgent()).GetState();
        
        // agent2 应该收到事件（CurrentHop=0->1）
        Assert.Equal("hop-test", agent2State.Name);
        
        // agent3 不应该收到事件（CurrentHop=1 已达到 MaxHop=1）
        Assert.NotEqual("hop-test", agent3State.Name);
        
        // Cleanup
        await agent1.DeactivateAsync();
        await agent2.DeactivateAsync();
        await agent3.DeactivateAsync();
    }

    [Fact]
    public async Task RemoveChild_ShouldUpdateHierarchy()
    {
        // Arrange
        var parentActor = await _factory.CreateAgentAsync<TestAgent, TestState>(Guid.NewGuid());
        var childActor = await _factory.CreateAgentAsync<TestAgent, TestState>(Guid.NewGuid());
        
        await parentActor.AddChildAsync(childActor.Id);
        
        // Act
        await parentActor.RemoveChildAsync(childActor.Id);
        
        // Assert
        var children = await parentActor.GetChildrenAsync();
        Assert.DoesNotContain(childActor.Id, children);
        
        // Cleanup
        await parentActor.DeactivateAsync();
        await childActor.DeactivateAsync();
    }
}
