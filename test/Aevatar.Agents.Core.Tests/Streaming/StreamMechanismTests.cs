using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Core.Tests.Messages;
using Aevatar.Agents.Local;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Xunit;
using Moq;
using Google.Protobuf;

namespace Aevatar.Agents.Core.Tests.Streaming;

/// <summary>
/// 测试新的向上回响Stream机制
/// </summary>
public class StreamMechanismTests
{
    private readonly LocalGAgentActorManager _manager;
    private readonly ILogger<StreamMechanismTests> _logger;
    private readonly ServiceProvider _serviceProvider;
    
    public StreamMechanismTests()
    {
        // 创建真实的服务容器
        var services = new ServiceCollection();
        
        // 注册日志
        services.AddLogging(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        
        // 注意：不需要注册Agent类型，它们由Factory直接创建
        // Agent实例应该由Factory管理，而不是从DI容器获取
        
        // 使用自动发现模式，无需手动注册
        services.AddGAgentActorFactoryProvider();
        
        // 注册工厂和管理器
        services.AddSingleton<LocalGAgentActorFactory>();
        services.AddSingleton<IGAgentActorFactory>(sp => sp.GetRequiredService<LocalGAgentActorFactory>());
        services.AddSingleton<LocalGAgentActorManager>();
        services.AddSingleton<IGAgentActorManager>(sp => sp.GetRequiredService<LocalGAgentActorManager>());
        
        _serviceProvider = services.BuildServiceProvider();
        
        _logger = _serviceProvider.GetRequiredService<ILogger<StreamMechanismTests>>();
        _manager = _serviceProvider.GetRequiredService<LocalGAgentActorManager>();
    }
    
    #region 父子关系订阅测试
    
    /// <summary>
    /// 测试SetParent时自动订阅父stream
    /// </summary>
    [Fact]
    [DisplayName("Setting parent should automatically subscribe to parent's stream")]
    public async Task SetParent_Should_Subscribe_To_Parent_Stream()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        
        var parentActor = await _manager.CreateAndRegisterAsync<TestParentAgent>(
            parentId, CancellationToken.None);
        var childActor = await _manager.CreateAndRegisterAsync<TestChildAgent>(
            childId, CancellationToken.None);
        
        // Act
        await childActor.SetParentAsync(parentId);
        await parentActor.AddChildAsync(childId);
        
        // 父节点发布DOWN事件
        var parentAgent = parentActor.GetAgent() as TestParentAgent;
        await parentAgent!.PublishTestEvent("Hello Children");
        
        // 等待事件传播
        await Task.Delay(1000); // 增加等待时间确保事件处理完成
        
        // Assert
        var childAgent = childActor.GetAgent() as TestChildAgent;
        Assert.Contains("Hello Children", childAgent!.ReceivedEvents);
    }
    
    /// <summary>
    /// 测试ClearParent时自动取消订阅
    /// </summary>
    [Fact]
    [DisplayName("Clearing parent should automatically unsubscribe from parent's stream")]
    public async Task ClearParent_Should_Unsubscribe_From_Parent_Stream()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        
        var parentActor = await _manager.CreateAndRegisterAsync<TestParentAgent>(
            parentId, CancellationToken.None);
        var childActor = await _manager.CreateAndRegisterAsync<TestChildAgent>(
            childId, CancellationToken.None);
        
        await childActor.SetParentAsync(parentId);
        await parentActor.AddChildAsync(childId);
        
        // Act
        await childActor.ClearParentAsync();
        await parentActor.RemoveChildAsync(childId);  // 父节点也需要移除子节点
        
        // 父节点发布事件（子节点不应该收到）
        var parentAgent = parentActor.GetAgent() as TestParentAgent;
        await parentAgent!.PublishTestEvent("After Unsubscribe");
        
        await Task.Delay(1000); // 增加等待时间确保事件处理完成
        
        // Assert
        var childAgent = childActor.GetAgent() as TestChildAgent;
        Assert.DoesNotContain("After Unsubscribe", childAgent!.ReceivedEvents);
    }
    
    #endregion
    
    #region 事件传播方向测试
    
    /// <summary>
    /// 测试UP方向：子发布到父stream，自动广播给siblings
    /// </summary>
    [Fact]
    [DisplayName("UP direction: event should be broadcast to all siblings")]
    public async Task UP_Direction_Should_Broadcast_To_All_Siblings()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var child1Id = Guid.NewGuid();
        var child2Id = Guid.NewGuid();
        var child3Id = Guid.NewGuid();
        
        var parentActor = await _manager.CreateAndRegisterAsync<TestParentAgent>(
            parentId, CancellationToken.None);
        var child1Actor = await _manager.CreateAndRegisterAsync<TestChildAgent>(
            child1Id, CancellationToken.None);
        var child2Actor = await _manager.CreateAndRegisterAsync<TestChildAgent>(
            child2Id, CancellationToken.None);
        var child3Actor = await _manager.CreateAndRegisterAsync<TestChildAgent>(
            child3Id, CancellationToken.None);
        
        // 立即获取Agent并打印HashCode
        var immediateChild1 = child1Actor.GetAgent() as TestChildAgent;
        var immediateChild2 = child2Actor.GetAgent() as TestChildAgent;
        var immediateChild3 = child3Actor.GetAgent() as TestChildAgent;
        Console.WriteLine($"[IMMEDIATE] child1Agent HashCode={immediateChild1!.GetHashCode()}, Id={child1Id}");
        Console.WriteLine($"[IMMEDIATE] child2Agent HashCode={immediateChild2!.GetHashCode()}, Id={child2Id}");
        Console.WriteLine($"[IMMEDIATE] child3Agent HashCode={immediateChild3!.GetHashCode()}, Id={child3Id}");
        
        // 建立父子关系
        await child1Actor.SetParentAsync(parentId);
        await child2Actor.SetParentAsync(parentId);
        await child3Actor.SetParentAsync(parentId);
        await parentActor.AddChildAsync(child1Id);
        await parentActor.AddChildAsync(child2Id);
        await parentActor.AddChildAsync(child3Id);
        
        // Act
        var child1Agent = child1Actor.GetAgent() as TestChildAgent;
        await child1Agent!.PublishUpEvent("Message from Child1");
        
        await Task.Delay(1000); // 增加等待时间确保事件处理完成
        
        // Assert - 所有siblings都应该收到消息
        var child2Agent = child2Actor.GetAgent() as TestChildAgent;
        var child3Agent = child3Actor.GetAgent() as TestChildAgent;
        var parentAgent = parentActor.GetAgent() as TestParentAgent;
        
        // 打印调试信息
        Console.WriteLine($"[TEST] child1Agent HashCode={child1Agent.GetHashCode()}, ReceivedEvents={child1Agent.ReceivedEvents.Count}");
        Console.WriteLine($"[TEST] child2Agent HashCode={child2Agent!.GetHashCode()}, ReceivedEvents={child2Agent.ReceivedEvents.Count}");
        Console.WriteLine($"[TEST] child3Agent HashCode={child3Agent!.GetHashCode()}, ReceivedEvents={child3Agent.ReceivedEvents.Count}");
        Console.WriteLine($"[TEST] parentAgent HashCode={parentAgent!.GetHashCode()}, ReceivedEvents={parentAgent.ReceivedEvents.Count}");
        
        Assert.Contains("Message from Child1", child1Agent.ReceivedEvents); // 包括自己
        Assert.Contains("Message from Child1", child2Agent!.ReceivedEvents);
        Assert.Contains("Message from Child1", child3Agent!.ReceivedEvents);
        Assert.Contains("Message from Child1", parentAgent!.ReceivedEvents); // 父也收到
    }
    
    /// <summary>
    /// 测试DOWN方向：父发布到自己stream，广播给children
    /// </summary>
    [Fact(Timeout = 5000)] // 添加5秒超时保护，防止栈溢出导致的无限等待
    [DisplayName("DOWN direction: event should be broadcast to all children")]
    public async Task DOWN_Direction_Should_Broadcast_To_All_Children()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var child1Id = Guid.NewGuid();
        var child2Id = Guid.NewGuid();
        
        var parentActor = await _manager.CreateAndRegisterAsync<TestParentAgent>(
            parentId, CancellationToken.None);
        var child1Actor = await _manager.CreateAndRegisterAsync<TestChildAgent>(
            child1Id, CancellationToken.None);
        var child2Actor = await _manager.CreateAndRegisterAsync<TestChildAgent>(
            child2Id, CancellationToken.None);
        
        // 建立父子关系
        await child1Actor.SetParentAsync(parentId);
        await child2Actor.SetParentAsync(parentId);
        await parentActor.AddChildAsync(child1Id);
        await parentActor.AddChildAsync(child2Id);
        
        // Act
        var parentAgent = parentActor.GetAgent() as TestParentAgent;
        await parentAgent!.PublishDownEvent("Announcement from Parent");
        
        await Task.Delay(1000); // 增加等待时间确保事件处理完成
        
        // Assert - 所有children都应该收到
        var child1Agent = child1Actor.GetAgent() as TestChildAgent;
        var child2Agent = child2Actor.GetAgent() as TestChildAgent;
        
        Assert.Contains("Announcement from Parent", child1Agent!.ReceivedEvents);
        Assert.Contains("Announcement from Parent", child2Agent!.ReceivedEvents);
        Assert.Contains("Announcement from Parent", parentAgent.ReceivedEvents); // 父自己也收到
    }
    
    /// <summary>
    /// 测试BOTH方向：同时向上和向下
    /// </summary>
    [Fact(Timeout = 5000)] // 添加5秒超时保护
    [DisplayName("BOTH direction: event should be broadcast in both directions")]
    public async Task BOTH_Direction_Should_Broadcast_In_Both_Directions()
    {
        // Arrange - 三层结构：grandparent -> parent -> children
        var grandparentId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var child1Id = Guid.NewGuid();
        var child2Id = Guid.NewGuid();
        
        var grandparentActor = await _manager.CreateAndRegisterAsync<TestParentAgent>(
            grandparentId, CancellationToken.None);
        var parentActor = await _manager.CreateAndRegisterAsync<TestParentAgent>(
            parentId, CancellationToken.None);
        var child1Actor = await _manager.CreateAndRegisterAsync<TestChildAgent>(
            child1Id, CancellationToken.None);
        var child2Actor = await _manager.CreateAndRegisterAsync<TestChildAgent>(
            child2Id, CancellationToken.None);
        
        // 建立层级关系
        await parentActor.SetParentAsync(grandparentId);
        await grandparentActor.AddChildAsync(parentId);
        
        await child1Actor.SetParentAsync(parentId);
        await child2Actor.SetParentAsync(parentId);
        await parentActor.AddChildAsync(child1Id);
        await parentActor.AddChildAsync(child2Id);
        
        // Act - parent发布BOTH事件
        var parentAgent = parentActor.GetAgent() as TestParentAgent;
        await parentAgent!.PublishBothEvent("Both Direction Message");
        
        await Task.Delay(1000); // 增加等待时间确保事件处理完成
        
        // Assert - grandparent和children都应该收到
        var grandparentAgent = grandparentActor.GetAgent() as TestParentAgent;
        var child1Agent = child1Actor.GetAgent() as TestChildAgent;
        var child2Agent = child2Actor.GetAgent() as TestChildAgent;
        
        Assert.Contains("Both Direction Message", grandparentAgent!.ReceivedEvents);
        Assert.Contains("Both Direction Message", parentAgent.ReceivedEvents);
        Assert.Contains("Both Direction Message", child1Agent!.ReceivedEvents);
        Assert.Contains("Both Direction Message", child2Agent!.ReceivedEvents);
    }
    
    #endregion
    
    #region Resume机制测试
    
    /// <summary>
    /// 测试订阅的暂停和恢复
    /// </summary>
    [Fact]
    [DisplayName("Resume mechanism should restore subscriptions")]
    public async Task Resume_Should_Restore_Subscription()
    {
        // Arrange
        var streamRegistry = new LocalMessageStreamRegistry();
        var parentStream = streamRegistry.GetOrCreateStream(Guid.NewGuid());
        
        var receivedMessages = new List<string>();
        var subscription = await parentStream.SubscribeAsync<EventEnvelope>(
            async envelope =>
            {
                receivedMessages.Add(envelope.Message);
                await Task.CompletedTask;
            });
        
        // Act - 发送第一个消息
        await parentStream.ProduceAsync(new EventEnvelope { Message = "Message 1" });
        await Task.Delay(50);
        
        // 暂停订阅
        await subscription.UnsubscribeAsync();
        
        // 发送第二个消息（不应该收到）
        await parentStream.ProduceAsync(new EventEnvelope { Message = "Message 2" });
        await Task.Delay(50);
        
        // 恢复订阅
        await subscription.ResumeAsync();
        
        // 发送第三个消息（应该收到）
        await parentStream.ProduceAsync(new EventEnvelope { Message = "Message 3" });
        await Task.Delay(50);
        
        // Assert
        Assert.Contains("Message 1", receivedMessages);
        Assert.DoesNotContain("Message 2", receivedMessages);
        Assert.Contains("Message 3", receivedMessages);
    }
    
    #endregion
    
    #region 类型过滤测试
    
    /// <summary>
    /// 测试基于TEvent的类型过滤
    /// </summary>
    [Fact]
    [DisplayName("Type filtering should only process matching event types")]
    public async Task Type_Filtering_Should_Only_Process_Matching_Events()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var specificChildId = Guid.NewGuid();
        var generalChildId = Guid.NewGuid();
        
        // SpecificChildAgent只处理TestSpecificEvent
        var parentActor = await _manager.CreateAndRegisterAsync<TestParentAgent>(
            parentId, CancellationToken.None);
        var specificChild = await _manager.CreateAndRegisterAsync<TestSpecificChildAgent>(
            specificChildId, CancellationToken.None);
        var generalChild = await _manager.CreateAndRegisterAsync<TestChildAgent>(
            generalChildId, CancellationToken.None);
        
        // 建立关系
        await specificChild.SetParentAsync(parentId);
        await generalChild.SetParentAsync(parentId);
        await parentActor.AddChildAsync(specificChildId);
        await parentActor.AddChildAsync(generalChildId);
        
        // Act - 发送不同类型的事件
        var parentAgent = parentActor.GetAgent() as TestParentAgent;
        await parentAgent!.PublishTestEvent("General Event");
        await parentAgent.PublishSpecificEvent("Specific Event");
        
        await Task.Delay(1000); // 增加等待时间确保事件处理完成
        
        // Assert
        var specificAgent = specificChild.GetAgent() as TestSpecificChildAgent;
        var generalAgent = generalChild.GetAgent() as TestChildAgent;
        
        // Specific child只收到specific event
        Assert.DoesNotContain("General Event", specificAgent!.ReceivedEvents);
        Assert.Contains("Specific Event", specificAgent.ReceivedEvents);
        
        // General child收到所有事件
        Assert.Contains("General Event", generalAgent!.ReceivedEvents);
        Assert.Contains("Specific Event", generalAgent.ReceivedEvents);
    }
    
    #endregion
    
    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}

#region Test Agents

// 注意：TestEvent, TestSpecificEvent, TestState 现在从 test_messages.proto 生成

// 父节点Agent
public class TestParentAgent : GAgentBase<Messages.TestState>
{
    public List<string> ReceivedEvents { get; } = new();
    
    public TestParentAgent(Guid id) : base(id) { }
    
    public override Task<string> GetDescriptionAsync() => 
        Task.FromResult("Test Parent Agent");
    
    public async Task PublishTestEvent(string content)
    {
        await PublishAsync(new TestEvent { Content = content }, EventDirection.Down);
    }
    
    public async Task PublishSpecificEvent(string content)
    {
        await PublishAsync(new TestSpecificEvent { Content = content }, EventDirection.Down);
    }
    
    public async Task PublishDownEvent(string content)
    {
        await PublishAsync(new TestEvent { Content = content }, EventDirection.Down);
    }
    
    public async Task PublishBothEvent(string content)
    {
        await PublishAsync(new TestEvent { Content = content }, EventDirection.Both);
    }
    
    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleTestEvent(TestEvent evt)
    {
        Console.WriteLine($"[DEBUG] {GetType().Name}.HandleTestEvent on instance {GetHashCode()}: Content='{evt?.Content}', EventId='{evt?.EventId}'");
        if (!string.IsNullOrEmpty(evt?.Content))
        {
            ReceivedEvents.Add(evt.Content);
        }
        else
        {
            Console.WriteLine($"[WARNING] Content is null or empty!");
        }
        Console.WriteLine($"[DEBUG] {GetType().Name} instance {GetHashCode()} ReceivedEvents now has {ReceivedEvents.Count} items");
        await Task.CompletedTask;
    }
    
    [EventHandler]
    public async Task HandleSpecificEvent(TestSpecificEvent evt)
    {
        ReceivedEvents.Add(evt.Content);
        await Task.CompletedTask;
    }
}

// 子节点Agent
public class TestChildAgent : GAgentBase<Messages.TestState>
{
    public List<string> ReceivedEvents { get; } = new();
    
    public TestChildAgent(Guid id) : base(id) { }
    
    public override Task<string> GetDescriptionAsync() => 
        Task.FromResult("Test Child Agent");
    
    public async Task PublishUpEvent(string content)
    {
        await PublishAsync(new TestEvent { Content = content }, EventDirection.Up);
    }
    
    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleTestEvent(TestEvent evt)
    {
        Console.WriteLine($"[DEBUG] {GetType().Name}.HandleTestEvent on instance {GetHashCode()}: Content='{evt?.Content}', EventId='{evt?.EventId}'");
        if (!string.IsNullOrEmpty(evt?.Content))
        {
            ReceivedEvents.Add(evt.Content);
        }
        else
        {
            Console.WriteLine($"[WARNING] Content is null or empty!");
        }
        Console.WriteLine($"[DEBUG] {GetType().Name} instance {GetHashCode()} ReceivedEvents now has {ReceivedEvents.Count} items");
        await Task.CompletedTask;
    }
    
    [EventHandler]
    public async Task HandleSpecificEvent(TestSpecificEvent evt)
    {
        ReceivedEvents.Add(evt.Content);
        await Task.CompletedTask;
    }
}

// 只处理特定事件的子节点Agent
public class TestSpecificChildAgent : GAgentBase<Messages.TestState, Messages.TestSpecificEvent>
{
    public List<string> ReceivedEvents { get; } = new();
    
    public TestSpecificChildAgent(Guid id) : base(id) { }
    
    public override Task<string> GetDescriptionAsync() => 
        Task.FromResult("Test Specific Child Agent");
    
    [EventHandler]
    public async Task HandleSpecificEvent(TestSpecificEvent evt)
    {
        ReceivedEvents.Add(evt.Content);
        await Task.CompletedTask;
    }
}

#endregion