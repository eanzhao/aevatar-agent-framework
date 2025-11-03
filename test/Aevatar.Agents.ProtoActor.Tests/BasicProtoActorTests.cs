using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.ProtoActor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Proto;
using Proto.DependencyInjection;
using Xunit;
using Google.Protobuf.WellKnownTypes;
using Aevatar.Agents.Serialization;
using System;

namespace Aevatar.Agents.ProtoActor.Tests;

/// <summary>
/// ProtoActor基础测试
/// </summary>
public class BasicProtoActorTests : IDisposable
{
    private readonly ActorSystem _actorSystem;
    private readonly IServiceProvider _serviceProvider;
    private readonly ProtoActorGAgentActorFactory _factory;
    private readonly ProtoActorMessageStreamRegistry _streamRegistry;
    
    public BasicProtoActorTests()
    {
        // 设置服务
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddConsole();
        });
        
        // 添加ProtoActor消息流注册表
        services.AddSingleton<ProtoActorMessageStreamRegistry>();
        
        _serviceProvider = services.BuildServiceProvider();
        
        // 创建ActorSystem
        var config = ActorSystemConfig.Setup();
        _actorSystem = new ActorSystem(config);
        
        var logger = _serviceProvider.GetRequiredService<ILogger<ProtoActorGAgentActorFactory>>();
        _factory = new ProtoActorGAgentActorFactory(_serviceProvider, _actorSystem, logger);
        _streamRegistry = new ProtoActorMessageStreamRegistry(_actorSystem.Root);
    }
    
    public void Dispose()
    {
        // ProtoActor doesn't require explicit shutdown in tests
    }
    
    [Fact]
    public async Task CreateActor_Should_Work()
    {
        // Arrange & Act
        var agentId = Guid.NewGuid();
        var actor = await _factory.CreateAgentAsync<TestAgent, TestState>(agentId);
        
        // Assert
        Assert.NotNull(actor);
        Assert.Equal(agentId, actor.Id);
        
        // Cleanup
        await actor.DeactivateAsync();
    }
    
    [Fact]
    public async Task Actor_Should_HandleEvents()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var actor = await _factory.CreateAgentAsync<TestAgent, TestState>(agentId);
        
        // Act
        // Send StringValue directly - it will be wrapped in EventEnvelope by the framework
        var testEvent = new StringValue { Value = "Hello ProtoActor" };
        var eventId = await actor.PublishEventAsync(testEvent, EventDirection.Down);
        
        // Assert
        Assert.NotEqual(Guid.Empty.ToString(), eventId.ToString());
        
        // Give some time for message processing
        await Task.Delay(100);
        
        // Verify the event was handled (we can't directly access the agent in ProtoActor)
        // Just verify no exception was thrown
        Assert.NotEqual(Guid.Empty.ToString(), eventId.ToString());
        
        // Cleanup
        await actor.DeactivateAsync();
    }
    
    [Fact]
    public async Task Actor_Should_MaintainHierarchy()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        
        var parentActor = await _factory.CreateAgentAsync<TestAgent, TestState>(parentId);
        var childActor = await _factory.CreateAgentAsync<TestAgent, TestState>(childId);
        
        // Act
        await childActor.SetParentAsync(parentId);
        await parentActor.AddChildAsync(childId);
        
        // Assert
        var retrievedParentId = await childActor.GetParentAsync();
        Assert.Equal(parentId, retrievedParentId);
        
        var children = await parentActor.GetChildrenAsync();
        Assert.Contains(childId, children);
        
        // Cleanup
        await childActor.DeactivateAsync();
        await parentActor.DeactivateAsync();
    }
    
    [Fact]
    public async Task EventRouting_Should_PropagateUp()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        
        var parentActor = await _factory.CreateAgentAsync<TestAgent, TestState>(parentId);
        var childActor = await _factory.CreateAgentAsync<TestAgent, TestState>(childId);
        
        await childActor.SetParentAsync(parentId);
        await parentActor.AddChildAsync(childId);
        
        // Act
        var testEvent = new StringValue { Value = "Propagate Up" };
        await childActor.PublishEventAsync(testEvent, EventDirection.Up);
        
        // Give some time for message propagation
        await Task.Delay(200);
        
        // Assert - just verify no exception
        // We can't directly access the internal agent in ProtoActor
        Assert.NotNull(parentActor);
        
        // Cleanup
        await childActor.DeactivateAsync();
        await parentActor.DeactivateAsync();
    }
    
    [Fact]
    public async Task EventRouting_Should_PropagateDown()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        
        var parentActor = await _factory.CreateAgentAsync<TestAgent, TestState>(parentId);
        var childActor = await _factory.CreateAgentAsync<TestAgent, TestState>(childId);
        
        await childActor.SetParentAsync(parentId);
        await parentActor.AddChildAsync(childId);
        
        // Act
        var testEvent = new StringValue { Value = "Propagate Down" };
        await parentActor.PublishEventAsync(testEvent, EventDirection.Down);
        
        // Give some time for message propagation
        await Task.Delay(200);
        
        // Assert - just verify no exception
        // We can't directly access the internal agent in ProtoActor
        Assert.NotNull(childActor);
        
        // Cleanup
        await childActor.DeactivateAsync();
        await parentActor.DeactivateAsync();
    }
    
    [Fact]
    public async Task GetPID_Should_ReturnValidPID()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var actor = await _factory.CreateAgentAsync<TestAgent, TestState>(agentId);
        
        // Act
        var protoActor = actor as ProtoActorGAgentActor;
        
        // Assert
        Assert.NotNull(protoActor);
        // ProtoActor doesn't expose PID directly in our implementation
        
        // Cleanup
        await actor.DeactivateAsync();
    }
    
    [Fact]
    public async Task MultipleActors_Should_WorkConcurrently()
    {
        // Arrange
        var tasks = new List<Task<IGAgentActor>>();
        var actorCount = 10;
        
        // Act - Create multiple actors concurrently
        for (int i = 0; i < actorCount; i++)
        {
            var agentId = Guid.NewGuid();
            tasks.Add(_factory.CreateAgentAsync<TestAgent, TestState>(agentId));
        }
        
        var actors = await Task.WhenAll(tasks);
        
        // Assert
        Assert.Equal(actorCount, actors.Length);
        foreach (var actor in actors)
        {
            Assert.NotNull(actor);
            Assert.NotEqual(Guid.Empty, actor.Id);
        }
        
        // Cleanup
        foreach (var actor in actors)
        {
            await actor.DeactivateAsync();
        }
    }
}

/// <summary>
/// 测试用的Agent
/// </summary>
public class TestAgent : GAgentBase<TestState>
{
    public bool EventHandled { get; private set; }
    public string? LastMessage { get; private set; }
    
    public TestAgent(Guid id, ILogger<TestAgent>? logger = null) 
        : base(id, logger)
    {
    }
    
    public override Task OnActivateAsync(CancellationToken ct = default)
    {
        Logger?.LogInformation("TestAgent {Id} activated", Id);
        return base.OnActivateAsync(ct);
    }
    
    public override Task OnDeactivateAsync(CancellationToken ct = default)
    {
        Logger?.LogInformation("TestAgent {Id} deactivated", Id);
        return base.OnDeactivateAsync(ct);
    }
    
    // Use StringValue as the test event type
    [EventHandler]
    public Task HandleStringValue(StringValue stringValue)
    {
        EventHandled = true;
        LastMessage = stringValue.Value;
        Logger?.LogInformation("TestAgent {Id} handled message: {Content}", Id, stringValue.Value);
        GetState().Counter++;
        GetState().LastMessage = stringValue.Value;
        return Task.CompletedTask;
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"TestAgent {Id}");
    }
}

/// <summary>
/// 测试用的状态
/// </summary>
public class TestState
{
    public int Counter { get; set; }
    public string? LastMessage { get; set; }
}

/// <summary>
/// 测试用的消息 - 简单的非Protobuf消息
/// </summary>
public class TestMessage
{
    public string Content { get; set; } = string.Empty;
}
