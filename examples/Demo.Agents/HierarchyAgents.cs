using Aevatar.Agents;
using Aevatar.Agents.Core;
using Aevatar.Agents.Abstractions.Attributes;
using Microsoft.Extensions.Logging;

namespace Demo.Agents;

// 管理者Agent
public class ManagerAgent : GAgentBase<ManagerState>
{
    public ManagerAgent(Guid id, ILogger<ManagerAgent>? logger = null) 
        : base(id, logger)
    {
    }
    
    [AllEventHandler]
    public Task HandleManagementEvent(EventEnvelope envelope)
    {
        // proto中没有EventsReceived字段，使用TeamSize代替
        State.TeamSize++;
        Logger?.LogInformation("Manager {Id} handling management event", Id);
        return Task.CompletedTask;
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Manager {Id}: Department {State.Department}, Team size: {State.TeamSize}");
    }
}

// ManagerState 已在 demo_messages.proto 中定义

// 员工Agent
public class EmployeeAgent : GAgentBase<EmployeeState>
{
    public EmployeeAgent(Guid id, ILogger<EmployeeAgent>? logger = null) 
        : base(id, logger)
    {
    }
    
    [AllEventHandler]
    public Task HandleWorkEvent(EventEnvelope envelope)
    {
        // proto中没有TasksCompleted字段
        State.Role = "Working";
        Logger?.LogInformation("Employee {Id} working on task", Id);
        return Task.CompletedTask;
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Employee {Id}: {State.Name}, Role: {State.Role}");
    }
}

// EmployeeState 已在 demo_messages.proto 中定义

// 层级Agent
public class HierarchyAgent : GAgentBase<HierarchyState>
{
    public HierarchyAgent(Guid id, ILogger<HierarchyAgent>? logger = null) 
        : base(id, logger)
    {
    }
    
    [EventHandler]
    public Task HandleHierarchyMessage(HierarchyMessage message)
    {
        // proto中没有MessagesReceived和LastMessageDirection字段
        State.Level++;
        Logger?.LogInformation("HierarchyAgent {Id} received hierarchy message: {Content}", 
            Id, message.Content);
        return Task.CompletedTask;
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"HierarchyAgent {Id}: Level {State.Level}, Children: {State.Children.Count}");
    }
}

// HierarchyState 已在 demo_messages.proto 中定义
