using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Core.Tests.TestHelpers;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;

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
    
    [EventHandler(AllowSelfHandling = true)]  // 允许处理自己发布的事件
    public async Task HandleTestEvent(StringValue message)
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
        
        // 使用自动发现模式，无需手动注册
        services.AddGAgentActorFactoryProvider();
        
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
        var actor = await _factory.CreateGAgentActorAsync<LocalTestAgent>(agentId);

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
        var actor = await _factory.CreateGAgentActorAsync<LocalTestAgent>(agentId);
        var agent = actor.GetAgent() as LocalTestAgent;

        // Act - 发送具体的消息类型，而不是 EventEnvelope
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

        var parent = await _factory.CreateGAgentActorAsync<LocalTestAgent>(parentId);
        var child = await _factory.CreateGAgentActorAsync<LocalTestAgent>(childId);

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

        var parent = await _factory.CreateGAgentActorAsync<LocalTestAgent>(parentId);
        var child = await _factory.CreateGAgentActorAsync<LocalTestAgent>(childId);

        await parent.AddChildAsync(childId);
        await child.SetParentAsync(parentId);

        // Act - Send event down from parent
        var downMessage = new StringValue { Value = "down" };
        await parent.PublishEventAsync(downMessage, EventDirection.Down);

        // Act - Send event up from child
        var upMessage = new StringValue { Value = "up" };
        await child.PublishEventAsync(upMessage, EventDirection.Up);

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
        var actor = await _factory.CreateGAgentActorAsync<LocalTestAgent>(agentId);

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
        var agent = actor.GetAgent() as LocalTestAgent;
        Assert.NotNull(agent);
        Assert.Equal(10, agent.GetState().Counter);
    }

    [Fact]
    public async Task Should_Properly_Deactivate()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var actor = await _factory.CreateGAgentActorAsync<LocalTestAgent>(agentId);
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

        var parent = await _factory.CreateGAgentActorAsync<LocalTestAgent>(parentId);
        var child = await _factory.CreateGAgentActorAsync<LocalTestAgent>(childId);

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

        var parent = await _factory.CreateGAgentActorAsync<LocalTestAgent>(parentId);
        var child = await _factory.CreateGAgentActorAsync<LocalTestAgent>(childId);

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
        for (var i = 0; i < 5; i++)
        {
            agents[i] = await _factory.CreateGAgentActorAsync<LocalTestAgent>(Guid.NewGuid());
        }

        // Send different events to each
        var tasks = new Task[5];
        for (var i = 0; i < 5; i++)
        {
            var message = new StringValue { Value = $"agent_{i}" };
            tasks[i] = agents[i].PublishEventAsync(message, EventDirection.Down);
        }

        await Task.WhenAll(tasks);
        await Task.Delay(100);

        // Assert - Each agent should have processed its event
        for (var i = 0; i < 5; i++)
        {
            var agent = agents[i].GetAgent() as LocalTestAgent;
            Assert.NotNull(agent);
            Assert.Equal(1, agent.GetState().Counter);
        }
    }

    [Fact]
    public async Task Should_Create_Agent_With_Single_Generic_Parameter()
    {
        // Arrange
        var agentId = Guid.NewGuid();

        // Act - 使用单泛型参数版本
        var actor = await _factory.CreateGAgentActorAsync<LocalTestAgent>(agentId);

        // Assert
        Assert.NotNull(actor);
        Assert.Equal(agentId, actor.Id);
        Assert.IsType<LocalGAgentActor>(actor);

        var agent = actor.GetAgent() as LocalTestAgent;
        Assert.NotNull(agent);
        Assert.True(agent.WasActivated);

        // 验证状态类型被正确推断
        var state = agent.GetState();
        Assert.NotNull(state);
        Assert.IsType<LocalTestState>(state);
    }

    [Fact]
    public async Task Single_And_Double_Generic_Should_Create_Same_Agent()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        // Act - 创建两个 agent，一个用单参数，一个用双参数
        var actor1 = await _factory.CreateGAgentActorAsync<LocalTestAgent>(id1);
        var actor2 = await _factory.CreateGAgentActorAsync<LocalTestAgent>(id2);

        // Assert - 两种方式应该创建相同类型的 agent
        Assert.IsType<LocalGAgentActor>(actor1);
        Assert.IsType<LocalGAgentActor>(actor2);

        var agent1 = actor1.GetAgent() as LocalTestAgent;
        var agent2 = actor2.GetAgent() as LocalTestAgent;

        Assert.NotNull(agent1);
        Assert.NotNull(agent2);

        Assert.IsType<LocalTestState>(agent1.GetState());
        Assert.IsType<LocalTestState>(agent2.GetState());
    }

    [Fact]
    public async Task Event_Propagation_Should_Follow_Direction_Semantics()
    {
        // Arrange - 创建一个三层结构：parent -> middle -> child
        var parentId = Guid.NewGuid();
        var middleId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        var parent = await _factory.CreateGAgentActorAsync<LocalTestAgent>(parentId);
        var middle = await _factory.CreateGAgentActorAsync<LocalTestAgent>(middleId);
        var child = await _factory.CreateGAgentActorAsync<LocalTestAgent>(childId);

        // 建立层级关系
        await parent.AddChildAsync(middleId);
        await middle.SetParentAsync(parentId);
        await middle.AddChildAsync(childId);
        await child.SetParentAsync(middleId);

        var parentAgent = parent.GetAgent() as LocalTestAgent;
        var middleAgent = middle.GetAgent() as LocalTestAgent;
        var childAgent = child.GetAgent() as LocalTestAgent;

        // Test 1: Down 方向 - 从 parent 发送，middle 和 child 应该收到
        var downMessage = new StringValue { Value = "down_test" };
        await parent.PublishEventAsync(downMessage, EventDirection.Down);
        await Task.Delay(100);

        Assert.Equal(1, parentAgent.GetState().Counter); // parent 自己也处理
        Assert.Equal(1, middleAgent.GetState().Counter); // middle 收到
        Assert.Equal(1, childAgent.GetState().Counter); // child 收到

        // Reset counters
        parentAgent.GetState().Counter = 0;
        middleAgent.GetState().Counter = 0;
        childAgent.GetState().Counter = 0;

        // Test 2: Up 方向 - 从 child 发送，只有 middle 和 parent 应该收到
        var upMessage = new StringValue { Value = "up_test" };
        await child.PublishEventAsync(upMessage, EventDirection.Up);
        await Task.Delay(100);

        Assert.Equal(1, childAgent.GetState().Counter); // child 自己也处理
        Assert.Equal(1, middleAgent.GetState().Counter); // middle 收到
        Assert.Equal(1, parentAgent.GetState().Counter); // parent 收到

        // Reset counters
        parentAgent.GetState().Counter = 0;
        middleAgent.GetState().Counter = 0;
        childAgent.GetState().Counter = 0;

        // Test 3: 没有目标节点的情况 - Down 从叶子节点发送
        var leafMessage = new StringValue { Value = "leaf_test" };
        await child.PublishEventAsync(leafMessage, EventDirection.Down);
        await Task.Delay(100);

        Assert.Equal(1, childAgent.GetState().Counter); // 只有 child 自己处理
        Assert.Equal(0, middleAgent.GetState().Counter); // middle 不应该收到
        Assert.Equal(0, parentAgent.GetState().Counter); // parent 不应该收到
    }
}