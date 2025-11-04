using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.EventRouting;
using Aevatar.Agents.Core.Tests.Messages;
using Microsoft.Extensions.Logging;
using Xunit;
using Google.Protobuf;

namespace Aevatar.Agents.Core.Tests.EventRouting;

/// <summary>
/// 测试新的EventDirection设计
/// </summary>
public class EventDirectionTests
{
    private readonly ILogger<EventDirectionTests> _logger;
    
    public EventDirectionTests()
    {
        var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _logger = loggerFactory.CreateLogger<EventDirectionTests>();
    }
    
    /// <summary>
    /// 验证EventDirection枚举值
    /// </summary>
    [Fact]
    public void EventDirection_Should_Have_Correct_Values()
    {
        // Assert
        Assert.Equal(0, (int)EventDirection.Unspecified);
        Assert.Equal(1, (int)EventDirection.Down);
        Assert.Equal(2, (int)EventDirection.Up);
        Assert.Equal(3, (int)EventDirection.Both);
        
        // 确认只有4个方向（新设计）
        var values = Enum.GetValues<EventDirection>();
        Assert.Equal(4, values.Length);
    }
    
    /// <summary>
    /// 测试UP方向的语义：向parent stream发送，自动广播给siblings
    /// </summary>
    [Fact]
    public async Task UP_Direction_Should_Send_To_Parent_Stream()
    {
        // Arrange
        var routedEvents = new List<(Guid ActorId, EventEnvelope Envelope)>();
        var router = new EventRouter(
            agentId: Guid.NewGuid(),
            sendToActorAsync: async (actorId, envelope, ct) =>
            {
                routedEvents.Add((actorId, envelope));
                await Task.CompletedTask;
            },
            sendToSelfAsync: async (envelope, ct) => await Task.CompletedTask,
            logger: _logger);
        
        var parentId = Guid.NewGuid();
        router.SetParent(parentId);
        
        var testEvent = new DirectionTestEvent { Content = "UP Event" };
        var envelope = router.CreateEventEnvelope(testEvent, EventDirection.Up);
        
        // Act
        await router.RouteEventAsync(envelope, CancellationToken.None);
        
        // Assert
        Assert.Single(routedEvents);
        Assert.Equal(parentId, routedEvents[0].ActorId);
        Assert.Equal(EventDirection.Up, routedEvents[0].Envelope.Direction);
        Assert.Equal("UP Event", ((DirectionTestEvent)testEvent).Content);
    }
    
    /// <summary>
    /// 测试DOWN方向的语义：向children发送
    /// </summary>
    [Fact]
    public async Task DOWN_Direction_Should_Send_To_Children()
    {
        // Arrange
        var routedEvents = new List<(Guid ActorId, EventEnvelope Envelope)>();
        var router = new EventRouter(
            agentId: Guid.NewGuid(),
            sendToActorAsync: async (actorId, envelope, ct) =>
            {
                routedEvents.Add((actorId, envelope));
                await Task.CompletedTask;
            },
            sendToSelfAsync: async (envelope, ct) => await Task.CompletedTask,
            logger: _logger);
        
        var child1Id = Guid.NewGuid();
        var child2Id = Guid.NewGuid();
        router.AddChild(child1Id);
        router.AddChild(child2Id);
        
        var testEvent = new DirectionTestEvent { Content = "DOWN Event" };
        var envelope = router.CreateEventEnvelope(testEvent, EventDirection.Down);
        
        // Act
        await router.RouteEventAsync(envelope, CancellationToken.None);
        
        // Assert
        Assert.Equal(2, routedEvents.Count);
        Assert.Contains(routedEvents, e => e.ActorId == child1Id);
        Assert.Contains(routedEvents, e => e.ActorId == child2Id);
        Assert.All(routedEvents, e => Assert.Equal(EventDirection.Down, e.Envelope.Direction));
    }
    
    /// <summary>
    /// 测试BOTH方向的语义：同时向上和向下
    /// </summary>
    [Fact]
    public async Task BOTH_Direction_Should_Send_Both_Ways()
    {
        // Arrange
        var routedEvents = new List<(Guid ActorId, EventEnvelope Envelope)>();
        var router = new EventRouter(
            agentId: Guid.NewGuid(),
            sendToActorAsync: async (actorId, envelope, ct) =>
            {
                routedEvents.Add((actorId, envelope));
                await Task.CompletedTask;
            },
            sendToSelfAsync: async (envelope, ct) => await Task.CompletedTask,
            logger: _logger);
        
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        router.SetParent(parentId);
        router.AddChild(childId);
        
        var testEvent = new DirectionTestEvent { Content = "BOTH Event" };
        var envelope = router.CreateEventEnvelope(testEvent, EventDirection.Both);
        
        // Act
        await router.RouteEventAsync(envelope, CancellationToken.None);
        
        // Assert
        Assert.Equal(2, routedEvents.Count);
        Assert.Contains(routedEvents, e => e.ActorId == parentId);
        Assert.Contains(routedEvents, e => e.ActorId == childId);
    }
    
    /// <summary>
    /// 测试没有parent时UP方向的行为
    /// </summary>
    [Fact]
    public async Task UP_Without_Parent_Should_Not_Route()
    {
        // Arrange
        var routedEvents = new List<(Guid ActorId, EventEnvelope Envelope)>();
        var router = new EventRouter(
            agentId: Guid.NewGuid(),
            sendToActorAsync: async (actorId, envelope, ct) =>
            {
                routedEvents.Add((actorId, envelope));
                await Task.CompletedTask;
            },
            sendToSelfAsync: async (envelope, ct) => await Task.CompletedTask,
            logger: _logger);
        
        // 没有设置parent
        var testEvent = new DirectionTestEvent { Content = "UP Event No Parent" };
        var envelope = router.CreateEventEnvelope(testEvent, EventDirection.Up);
        
        // Act
        await router.RouteEventAsync(envelope, CancellationToken.None);
        
        // Assert
        Assert.Empty(routedEvents); // 没有parent，事件不路由
    }
    
    /// <summary>
    /// 测试没有children时DOWN方向的行为
    /// </summary>
    [Fact]
    public async Task DOWN_Without_Children_Should_Not_Route()
    {
        // Arrange
        var routedEvents = new List<(Guid ActorId, EventEnvelope Envelope)>();
        var router = new EventRouter(
            agentId: Guid.NewGuid(),
            sendToActorAsync: async (actorId, envelope, ct) =>
            {
                routedEvents.Add((actorId, envelope));
                await Task.CompletedTask;
            },
            sendToSelfAsync: async (envelope, ct) => await Task.CompletedTask,
            logger: _logger);
        
        // 没有添加children
        var testEvent = new DirectionTestEvent { Content = "DOWN Event No Children" };
        var envelope = router.CreateEventEnvelope(testEvent, EventDirection.Down);
        
        // Act
        await router.RouteEventAsync(envelope, CancellationToken.None);
        
        // Assert
        Assert.Empty(routedEvents); // 没有children，事件不路由
    }
    
    /// <summary>
    /// 测试循环检测
    /// </summary>
    [Fact]
    public async Task Should_Prevent_Circular_Routing()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var routedCount = 0;
        
        var router = new EventRouter(
            agentId: agentId,
            sendToActorAsync: async (actorId, envelope, ct) =>
            {
                routedCount++;
                await Task.CompletedTask;
            },
            sendToSelfAsync: async (envelope, ct) => await Task.CompletedTask,
            logger: _logger);
        
        router.SetParent(parentId);
        
        var testEvent = new DirectionTestEvent { Content = "Circular Test" };
        var envelope = router.CreateEventEnvelope(testEvent, EventDirection.Up);
        
        // 模拟已经访问过parent（循环）
        envelope.Publishers.Add(parentId.ToString());
        
        // Act
        await router.RouteEventAsync(envelope, CancellationToken.None);
        
        // Assert
        Assert.Equal(0, routedCount); // 检测到循环，不路由
    }
    
    /// <summary>
    /// 测试最大跳数限制
    /// </summary>
    [Fact]
    public async Task Should_Respect_Max_Hop_Count()
    {
        // Arrange
        var routedEvents = new List<EventEnvelope>();
        var router = new EventRouter(
            agentId: Guid.NewGuid(),
            sendToActorAsync: async (actorId, envelope, ct) =>
            {
                routedEvents.Add(envelope);
                await Task.CompletedTask;
            },
            sendToSelfAsync: async (envelope, ct) => await Task.CompletedTask,
            logger: _logger);
        
        var child1Id = Guid.NewGuid();
        var child2Id = Guid.NewGuid();
        router.AddChild(child1Id);
        router.AddChild(child2Id);
        
        var testEvent = new DirectionTestEvent { Content = "Max Hop Test" };
        var envelope = router.CreateEventEnvelope(testEvent, EventDirection.Down);
        envelope.MaxHopCount = 1;
        envelope.CurrentHopCount = 0;
        
        // Act
        await router.RouteEventAsync(envelope, CancellationToken.None);
        
        // Assert
        Assert.Equal(2, routedEvents.Count);
        Assert.All(routedEvents, e => Assert.Equal(1, e.CurrentHopCount));
    }
}

// 注意：DirectionTestEvent 现在从 test_messages.proto 生成
