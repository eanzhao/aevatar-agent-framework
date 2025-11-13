using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Core.Tests.Messages;
using Aevatar.Agents.Runtime.Local;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core.Tests.Streaming;

/// <summary>
/// 集成测试：测试完整的Stream机制在实际场景中的表现
/// </summary>
public class IntegrationStreamTests : IDisposable
{
    private readonly LocalGAgentActorManager _manager;
    private readonly ILogger<IntegrationStreamTests> _logger;
    private readonly ServiceProvider _serviceProvider;
    
    public IntegrationStreamTests()
    {
        // 创建真实的服务容器
        var services = new ServiceCollection();
        
        // 注册日志
        services.AddLogging(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        
        // 注册测试用的Agent类型
        services.AddTransient<TeamLeader>();
        services.AddTransient<Developer>();
        services.AddTransient<Tester>();
        services.AddTransient<Executive>();
        services.AddTransient<Manager>();
        services.AddTransient<Employee>();
        
        // 使用自动发现模式，无需手动注册
        services.AddGAgentActorFactoryProvider();
        
        // 注册工厂和管理器
        services.AddSingleton<LocalGAgentActorFactory>();
        services.AddSingleton<IGAgentActorFactory>(sp => sp.GetRequiredService<LocalGAgentActorFactory>());
        services.AddSingleton<LocalGAgentActorManager>();
        services.AddSingleton<IGAgentActorManager>(sp => sp.GetRequiredService<LocalGAgentActorManager>());
        
        _serviceProvider = services.BuildServiceProvider();
        
        _logger = _serviceProvider.GetRequiredService<ILogger<IntegrationStreamTests>>();
        _manager = _serviceProvider.GetRequiredService<LocalGAgentActorManager>();
    }
    
    /// <summary>
    /// 场景1：团队协作 - 任务分配与进度同步
    /// </summary>
    [Fact]
    public async Task TeamCollaboration_Scenario_Works_Correctly()
    {
        // Arrange - 创建团队结构
        var teamLeaderId = Guid.NewGuid();
        var developer1Id = Guid.NewGuid();
        var developer2Id = Guid.NewGuid();
        var testerId = Guid.NewGuid();
        
        var leader = await _manager.CreateAndRegisterAsync<TeamLeader>(
            teamLeaderId, CancellationToken.None);
        var dev1 = await _manager.CreateAndRegisterAsync<Developer>(
            developer1Id, CancellationToken.None);
        var dev2 = await _manager.CreateAndRegisterAsync<Developer>(
            developer2Id, CancellationToken.None);
        var tester = await _manager.CreateAndRegisterAsync<Tester>(
            testerId, CancellationToken.None);
        
        // 建立团队层级
        await dev1.SetParentAsync(teamLeaderId);
        await dev2.SetParentAsync(teamLeaderId);
        await tester.SetParentAsync(teamLeaderId);
        await leader.AddChildAsync(developer1Id);
        await leader.AddChildAsync(developer2Id);
        await leader.AddChildAsync(testerId);
        
        // Act - 执行团队协作流程
        
        // 1. Leader分配任务（DOWN）
        var leaderAgent = leader.GetAgent() as TeamLeader;
        await leaderAgent!.AssignTask("TASK-001", "Implement login feature", developer1Id);
        await leaderAgent.AssignTask("TASK-002", "Write unit tests", developer2Id);
        
        await Task.Delay(100);
        
        // 2. Developer1完成任务并通知团队（UP）
        var dev1Agent = dev1.GetAgent() as Developer;
        await dev1Agent!.CompleteTask("TASK-001");
        
        await Task.Delay(100);
        
        // 3. Tester开始测试（UP通知）
        var testerAgent = tester.GetAgent() as Tester;
        await testerAgent!.StartTesting("TASK-001");
        
        await Task.Delay(100);
        
        // Assert - 验证协作结果
        
        // Leader应该知道所有进度
        Assert.Contains("TASK-001", leaderAgent.CompletedTasks);
        Assert.Contains("TASK-001", leaderAgent.TestingTasks);
        
        // 所有团队成员都应该知道TASK-001完成了（通过UP广播）
        var dev2Agent = dev2.GetAgent() as Developer;
        Assert.Contains("TASK-001", dev2Agent!.TeamCompletedTasks);
        Assert.Contains("TASK-001", testerAgent.TeamCompletedTasks);
        
        // Tester应该收到需要测试的任务
        Assert.Contains("TASK-001", testerAgent.TestingQueue);
        
        _logger.LogInformation("Team collaboration scenario completed successfully");
    }
    
    /// <summary>
    /// 场景2：多层级组织 - 信息传递
    /// </summary>
    [Fact]
    public async Task MultiLevel_Organization_Communication_Works()
    {
        // Arrange - 创建三层组织结构
        var ceoId = Guid.NewGuid();
        var manager1Id = Guid.NewGuid();
        var manager2Id = Guid.NewGuid();
        var employee1Id = Guid.NewGuid();
        var employee2Id = Guid.NewGuid();
        var employee3Id = Guid.NewGuid();
        
        var ceo = await _manager.CreateAndRegisterAsync<Executive>(
            ceoId, CancellationToken.None);
        var manager1 = await _manager.CreateAndRegisterAsync<Manager>(
            manager1Id, CancellationToken.None);
        var manager2 = await _manager.CreateAndRegisterAsync<Manager>(
            manager2Id, CancellationToken.None);
        var emp1 = await _manager.CreateAndRegisterAsync<Employee>(
            employee1Id, CancellationToken.None);
        var emp2 = await _manager.CreateAndRegisterAsync<Employee>(
            employee2Id, CancellationToken.None);
        var emp3 = await _manager.CreateAndRegisterAsync<Employee>(
            employee3Id, CancellationToken.None);
        
        // 建立组织层级
        // CEO -> Managers
        await manager1.SetParentAsync(ceoId);
        await manager2.SetParentAsync(ceoId);
        await ceo.AddChildAsync(manager1Id);
        await ceo.AddChildAsync(manager2Id);
        
        // Manager1 -> Emp1, Emp2
        await emp1.SetParentAsync(manager1Id);
        await emp2.SetParentAsync(manager1Id);
        await manager1.AddChildAsync(employee1Id);
        await manager1.AddChildAsync(employee2Id);
        
        // Manager2 -> Emp3
        await emp3.SetParentAsync(manager2Id);
        await manager2.AddChildAsync(employee3Id);
        
        // Act
        
        // 1. CEO发布公司公告（DOWN）
        var ceoAgent = ceo.GetAgent() as Executive;
        await ceoAgent!.AnnouncePolicy("New remote work policy");
        
        await Task.Delay(150);
        
        // 2. Employee1报告问题（UP）
        var emp1Agent = emp1.GetAgent() as Employee;
        await emp1Agent!.ReportIssue("System outage in production");
        
        await Task.Delay(150);
        
        // 3. Manager1发送团队更新（BOTH）
        var manager1Agent = manager1.GetAgent() as Manager;
        await manager1Agent!.SendTeamUpdate("Sprint planning tomorrow");
        
        await Task.Delay(150);
        
        // Assert
        
        // 所有人都应该收到CEO的公告
        var manager2Agent = manager2.GetAgent() as Manager;
        var emp2Agent = emp2.GetAgent() as Employee;
        var emp3Agent = emp3.GetAgent() as Employee;
        
        Assert.Contains("New remote work policy", manager1Agent!.ReceivedAnnouncements);
        Assert.Contains("New remote work policy", manager2Agent!.ReceivedAnnouncements);
        Assert.Contains("New remote work policy", emp1Agent.ReceivedAnnouncements);
        Assert.Contains("New remote work policy", emp2Agent!.ReceivedAnnouncements);
        Assert.Contains("New remote work policy", emp3Agent!.ReceivedAnnouncements);
        
        // Manager1和其团队成员应该知道production问题（UP广播）
        Assert.Contains("System outage in production", manager1Agent.TeamIssues);
        Assert.Contains("System outage in production", emp2Agent.TeamIssues);
        
        // Manager1的团队更新应该向上到CEO和向下到员工（BOTH）
        Assert.Contains("Sprint planning tomorrow", ceoAgent.ReceivedUpdates);
        Assert.Contains("Sprint planning tomorrow", emp1Agent.ReceivedUpdates);
        Assert.Contains("Sprint planning tomorrow", emp2Agent.ReceivedUpdates);
        
        _logger.LogInformation("Multi-level organization communication completed");
    }
    
    /// <summary>
    /// 场景3：动态重组 - 测试订阅管理
    /// </summary>
    [Fact]
    public async Task Dynamic_Reorganization_Handles_Subscription_Changes()
    {
        // Arrange
        var oldManagerId = Guid.NewGuid();
        var newManagerId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        
        var oldManager = await _manager.CreateAndRegisterAsync<Manager>(
            oldManagerId, CancellationToken.None);
        var newManager = await _manager.CreateAndRegisterAsync<Manager>(
            newManagerId, CancellationToken.None);
        var employee = await _manager.CreateAndRegisterAsync<Employee>(
            employeeId, CancellationToken.None);
        
        // 初始组织结构
        await employee.SetParentAsync(oldManagerId);
        await oldManager.AddChildAsync(employeeId);
        
        // Act
        
        // 1. 旧经理发送消息
        var oldManagerAgent = oldManager.GetAgent() as Manager;
        await oldManagerAgent!.SendTeamUpdate("Old manager update");
        
        await Task.Delay(100);
        
        var employeeAgent = employee.GetAgent() as Employee;
        Assert.Contains("Old manager update", employeeAgent!.ReceivedUpdates);
        
        // 2. 重组 - 员工换到新经理
        await employee.ClearParentAsync(); // 取消旧订阅
        await oldManager.RemoveChildAsync(employeeId);
        
        await employee.SetParentAsync(newManagerId); // 建立新订阅
        await newManager.AddChildAsync(employeeId);
        
        // 3. 旧经理再发消息（员工不应收到）
        await oldManagerAgent.SendTeamUpdate("Old manager update 2");
        await Task.Delay(100);
        
        // 4. 新经理发消息（员工应该收到）
        var newManagerAgent = newManager.GetAgent() as Manager;
        await newManagerAgent!.SendTeamUpdate("New manager update");
        await Task.Delay(100);
        
        // Assert
        Assert.DoesNotContain("Old manager update 2", employeeAgent.ReceivedUpdates);
        Assert.Contains("New manager update", employeeAgent.ReceivedUpdates);
        
        _logger.LogInformation("Dynamic reorganization scenario completed");
    }
    
    /// <summary>
    /// 场景4：并发压力测试
    /// </summary>
    [Fact(Skip = "Time consuming")]
    public async Task Concurrent_Message_Broadcasting_Works_Under_Load()
    {
        // Arrange - 创建较大的团队
        var leaderId = Guid.NewGuid();
        var leader = await _manager.CreateAndRegisterAsync<TeamLeader>(
            leaderId, CancellationToken.None);
        
        var memberActors = new List<IGAgentActor>();
        var memberIds = new List<Guid>();
        
        // 创建20个团队成员
        for (int i = 0; i < 20; i++)
        {
            var memberId = Guid.NewGuid();
            memberIds.Add(memberId);
            var member = await _manager.CreateAndRegisterAsync<Developer>(
                memberId, CancellationToken.None);
            memberActors.Add(member);
            
            await member.SetParentAsync(leaderId);
            await leader.AddChildAsync(memberId);
        }
        
        // Act - 并发发送消息
        var tasks = new List<Task>();
        var messageCount = 50;
        
        // 每个成员并发发送消息（UP）
        for (int i = 0; i < memberActors.Count; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                var dev = memberActors[index].GetAgent() as Developer;
                for (int j = 0; j < messageCount; j++)
                {
                    await dev!.SendTeamMessage($"Message {j} from Developer {index}");
                    await Task.Delay(Random.Shared.Next(10, 50));
                }
            }));
        }
        
        await Task.WhenAll(tasks);
        await Task.Delay(5000); // 等待所有消息传播
        
        // Assert - 验证消息广播
        var leaderAgent = leader.GetAgent() as TeamLeader;
        var totalExpectedMessages = memberActors.Count * messageCount;
        
        // Leader应该收到所有消息
        Assert.True(leaderAgent!.ReceivedMessages.Count >= totalExpectedMessages * 0.95, 
            $"Leader received {leaderAgent.ReceivedMessages.Count} out of {totalExpectedMessages} messages");
        
        // 抽查几个成员，确保他们收到了其他成员的消息
        for (int i = 0; i < 3; i++)
        {
            var dev = memberActors[i].GetAgent() as Developer;
            Assert.True(dev!.TeamMessages.Count > 0, 
                $"Developer {i} should have received team messages");
            
            // 验证收到了来自其他开发者的消息
            var otherDevMessages = dev.TeamMessages
                .Where(m => !m.Contains($"Developer {i}"))
                .ToList();
            Assert.True(otherDevMessages.Count > 0, 
                $"Developer {i} should have received messages from other developers");
        }
        
        _logger.LogInformation($"Concurrent test completed: {totalExpectedMessages} messages broadcast");
    }
    
    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}

// 注意：所有State和Event类型现在从 test_messages.proto 生成
// TeamState, DeveloperState, TesterState, ExecutiveState, ManagerState, EmployeeState
// TaskAssignmentEvent, TaskCompletedEvent, TestingStartedEvent, 
// AnnouncementEvent, TeamUpdateEvent, IssueReportEvent, TeamMessageEvent

// Agent实现
public class TeamLeader : GAgentBase<TeamState>
{
    public List<string> CompletedTasks { get; } = new();
    public List<string> TestingTasks { get; } = new();
    public ConcurrentBag<string> ReceivedMessages { get; } = new();
    
    public TeamLeader(Guid id) : base(id) { }
    public TeamLeader() : base() { }
    
    public override Task<string> GetDescriptionAsync() => Task.FromResult("Team Leader");
    
    public async Task AssignTask(string taskId, string description, Guid assignTo)
    {
        var evt = new TaskAssignmentEvent 
        { 
            TaskId = taskId, 
            Description = description,
            AssignedTo = assignTo.ToString()
        };
        await PublishAsync(evt, EventDirection.Down);
    }
    
    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleTaskCompleted(TaskCompletedEvent evt)
    {
        CompletedTasks.Add(evt.TaskId);
        await Task.CompletedTask;
    }
    
    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleTestingStarted(TestingStartedEvent evt)
    {
        TestingTasks.Add(evt.TaskId);
        await Task.CompletedTask;
    }
    
    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleTeamMessage(TeamMessageEvent evt)
    {
        ReceivedMessages.Add(evt.Message);
        await Task.CompletedTask;
    }
}

public class Developer : GAgentBase<Messages.DeveloperState>
{
    public List<string> MyTasks { get; } = new();
    public List<string> TeamCompletedTasks { get; } = new();
    public List<string> ReceivedAnnouncements { get; } = new();
    public List<string> ReceivedUpdates { get; } = new();
    public List<string> TeamIssues { get; } = new();
    public ConcurrentBag<string> TeamMessages { get; } = new();
    
    public Developer(Guid id) : base(id) { }
    public Developer() : base() { }
    
    public override Task<string> GetDescriptionAsync() => Task.FromResult("Developer");
    
    public async Task CompleteTask(string taskId)
    {
        await PublishAsync(new TaskCompletedEvent { TaskId = taskId }, EventDirection.Up);
    }
    
    public async Task SendTeamMessage(string message)
    {
        await PublishAsync(new TeamMessageEvent { Message = message }, EventDirection.Up);
    }
    
    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleTaskAssignment(TaskAssignmentEvent evt)
    {
        if (evt.AssignedTo == Id.ToString())
        {
            MyTasks.Add(evt.TaskId);
        }
        await Task.CompletedTask;
    }
    
    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleTaskCompleted(TaskCompletedEvent evt)
    {
        TeamCompletedTasks.Add(evt.TaskId);
        await Task.CompletedTask;
    }
    
    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleAnnouncement(AnnouncementEvent evt)
    {
        ReceivedAnnouncements.Add(evt.Content);
        await Task.CompletedTask;
    }
    
    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleTeamUpdate(TeamUpdateEvent evt)
    {
        ReceivedUpdates.Add(evt.Content);
        await Task.CompletedTask;
    }
    
    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleIssue(IssueReportEvent evt)
    {
        TeamIssues.Add(evt.Issue);
        await Task.CompletedTask;
    }
    
    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleTeamMessage(TeamMessageEvent evt)
    {
        TeamMessages.Add(evt.Message);
        await Task.CompletedTask;
    }
}

public class Tester : GAgentBase<Messages.TesterState>
{
    public List<string> TestingQueue { get; } = new();
    public List<string> TeamCompletedTasks { get; } = new();
    
    public Tester(Guid id) : base(id) { }
    public Tester() : base() { }
    
    public override Task<string> GetDescriptionAsync() => Task.FromResult("Tester");
    
    public async Task StartTesting(string taskId)
    {
        TestingQueue.Add(taskId);
        await PublishAsync(new TestingStartedEvent { TaskId = taskId }, EventDirection.Up);
    }
    
    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleTaskCompleted(TaskCompletedEvent evt)
    {
        TeamCompletedTasks.Add(evt.TaskId);
        TestingQueue.Add(evt.TaskId);
        await Task.CompletedTask;
    }
}

public class Executive : GAgentBase<Messages.ExecutiveState>
{
    public List<string> ReceivedUpdates { get; } = new();
    
    public Executive(Guid id) : base(id) { }
    public Executive() : base() { }
    
    public override Task<string> GetDescriptionAsync() => Task.FromResult("Executive");
    
    public async Task AnnouncePolicy(string policy)
    {
        await PublishAsync(new AnnouncementEvent { Content = policy }, EventDirection.Down);
    }
    
    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleTeamUpdate(TeamUpdateEvent evt)
    {
        ReceivedUpdates.Add(evt.Content);
        await Task.CompletedTask;
    }
}

public class Manager : GAgentBase<Messages.ManagerState>
{
    public List<string> ReceivedAnnouncements { get; } = new();
    public List<string> TeamIssues { get; } = new();
    
    public Manager(Guid id) : base(id) { }
    public Manager() : base() { }
    
    public override Task<string> GetDescriptionAsync() => Task.FromResult("Manager");
    
    public async Task SendTeamUpdate(string update)
    {
        await PublishAsync(new TeamUpdateEvent { Content = update }, EventDirection.Both);
    }
    
    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleAnnouncement(AnnouncementEvent evt)
    {
        ReceivedAnnouncements.Add(evt.Content);
        await Task.CompletedTask;
    }
    
    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleIssue(IssueReportEvent evt)
    {
        TeamIssues.Add(evt.Issue);
        await Task.CompletedTask;
    }
}

public class Employee : GAgentBase<Messages.EmployeeState>
{
    public List<string> ReceivedAnnouncements { get; } = new();
    public List<string> ReceivedUpdates { get; } = new();
    public List<string> TeamIssues { get; } = new();

    public Employee(Guid id) : base(id)
    {
    }

    public Employee() : base()
    {
    }

    public override Task<string> GetDescriptionAsync() => Task.FromResult("Employee");

    public async Task ReportIssue(string issue)
    {
        await PublishAsync(new IssueReportEvent { Issue = issue }, EventDirection.Up);
    }

    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleAnnouncement(AnnouncementEvent evt)
    {
        ReceivedAnnouncements.Add(evt.Content);
        await Task.CompletedTask;
    }

    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleTeamUpdate(TeamUpdateEvent evt)
    {
        ReceivedUpdates.Add(evt.Content);
        await Task.CompletedTask;
    }

    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleIssue(IssueReportEvent evt)
    {
        TeamIssues.Add(evt.Issue);
        await Task.CompletedTask;
    }
}
