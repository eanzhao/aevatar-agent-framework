using System;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Core.Tests.Agents;

#region Lifecycle Test Agent

/// <summary>
/// Agent for testing complete lifecycle
/// </summary>
public class LifecycleTestAgent : GAgentBase<LifecycleAgentState>
{
    public bool IsActivated { get; private set; }
    public bool IsDeactivated { get; private set; }
    public DateTime? ActivationTime { get; private set; }
    public DateTime? DeactivationTime { get; private set; }
    public int ProcessedEventCount { get; private set; }
    
    protected override Task OnActivateAsync(CancellationToken ct = default)
    {
        IsActivated = true;
        ActivationTime = DateTime.UtcNow;
        State.Status = "active";
        State.ActivatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
        return base.OnActivateAsync(ct);
    }
    
    protected override Task OnDeactivateAsync(CancellationToken ct = default)
    {
        IsDeactivated = true;
        DeactivationTime = DateTime.UtcNow;
        State.Status = "inactive";
        State.DeactivatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
        return base.OnDeactivateAsync(ct);
    }
    
    [EventHandler]
    public async Task HandleTestEvent(TestEvent evt)
    {
        ProcessedEventCount++;
        State.EventHistory.Add(evt.EventId);
        State.LastProcessedTime = Timestamp.FromDateTime(DateTime.UtcNow);
        await Task.CompletedTask;
    }
    
    public override string GetDescription()
    {
        return $"LifecycleAgent: {State.Status} (Processed: {ProcessedEventCount})";
    }
}

#endregion

#region Collaboration Test Agents

/// <summary>
/// Coordinator agent that assigns tasks
/// </summary>
public class CoordinatorAgent : GAgentBase<CoordinatorState>
{
    protected override Task OnActivateAsync(CancellationToken ct = default)
    {
        State.CoordinatorId = Id.ToString();
        State.IsActive = true;
        return base.OnActivateAsync(ct);
    }
    
    public async Task AssignTask(TaskAssignedEvent task)
    {
        State.AssignedTasks.Add(task.TaskId);
        await PublishAsync(task, EventDirection.Down);
    }
    
    public override string GetDescription()
    {
        return $"Coordinator: {State.CoordinatorId} (Tasks: {State.AssignedTasks.Count})";
    }
}

/// <summary>
/// Worker agent that processes tasks
/// </summary>
public class WorkerAgent : GAgentBase<WorkerState>
{
    public string WorkerId { get; set; } = string.Empty;
    
    protected override Task OnActivateAsync(CancellationToken ct = default)
    {
        State.WorkerId = WorkerId;
        State.IsAvailable = true;
        return base.OnActivateAsync(ct);
    }
    
    [EventHandler]
    public async Task HandleTaskAssignment(TaskAssignedEvent evt)
    {
        if (evt.AssignedTo == WorkerId)
        {
            State.ReceivedTasks.Add(evt.TaskId);
            State.IsAvailable = false;
            await Task.CompletedTask;
        }
    }
    
    public async Task CompleteTask(TaskCompletedEvent completion)
    {
        State.CompletedTasks.Add(completion.TaskId);
        State.IsAvailable = true;
        await PublishAsync(completion, EventDirection.Up);
    }
    
    public override string GetDescription()
    {
        return $"Worker: {WorkerId} (Completed: {State.CompletedTasks.Count})";
    }
}

/// <summary>
/// Aggregator agent that collects results
/// </summary>
public class AggregatorAgent : GAgentBase<AggregatorState>
{
    protected override Task OnActivateAsync(CancellationToken ct = default)
    {
        State.AggregatorId = Id.ToString();
        return base.OnActivateAsync(ct);
    }
    
    [EventHandler]
    public async Task HandleTaskCompleted(TaskCompletedEvent evt)
    {
        State.CollectedResults.Add($"{evt.TaskId}:{evt.Result}");
        State.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);
        await Task.CompletedTask;
    }
    
    public override string GetDescription()
    {
        return $"Aggregator: {State.AggregatorId} (Results: {State.CollectedResults.Count})";
    }
}

#endregion

#region Tree Structure Test Agents

/// <summary>
/// Tree node agent for hierarchical event propagation
/// </summary>
public class TreeNodeAgent : GAgentBase<TreeNodeState>
{
    public string NodeName { get; set; } = string.Empty;
    
    protected override Task OnActivateAsync(CancellationToken ct = default)
    {
        State.NodeName = NodeName;
        State.Level = 0; // Will be set based on tree position
        return base.OnActivateAsync(ct);
    }
    
    public void SetParent(string parentName)
    {
        State.ParentNode = parentName;
    }
    
    public void AddChild(string childName)
    {
        State.ChildNodes.Add(childName);
    }
    
    public async Task BroadcastDown(TreeBroadcastEvent evt)
    {
        State.SentBroadcasts.Add($"{evt.OriginNode}:{evt.Message}");
        await PublishAsync(evt, EventDirection.Down);
    }
    
    public async Task BroadcastUp(TreeBroadcastEvent evt)
    {
        State.SentBroadcasts.Add($"{evt.OriginNode}:{evt.Message}");
        await PublishAsync(evt, EventDirection.Up);
    }
    
    [EventHandler]
    public async Task HandleBroadcast(TreeBroadcastEvent evt)
    {
        State.ReceivedBroadcasts.Add($"{evt.OriginNode}:{evt.Message}");
        
        // Propagate if needed
        if (evt.Direction == "down" && State.ChildNodes.Count > 0)
        {
            var propagatedEvent = new TreeBroadcastEvent
            {
                Message = evt.Message,
                OriginNode = NodeName,
                Direction = "down"
            };
            await BroadcastDown(propagatedEvent);
        }
        
        await Task.CompletedTask;
    }
    
    public override string GetDescription()
    {
        return $"TreeNode: {NodeName} (Children: {State.ChildNodes.Count})";
    }
}

#endregion

#region Stateful Test Agent

/// <summary>
/// Stateful agent for state recovery testing
/// </summary>
public class StatefulAgent(Guid agentId) : GAgentBase<TestAgentState>(agentId)
{
    protected override Task OnActivateAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(State.Name))
        {
            State.Name = "NewStatefulAgent";
            State.Counter = 0;
            State.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);
        }
        return base.OnActivateAsync(ct);
    }
    
    [EventHandler]
    public async Task HandleTestEvent(TestEvent evt)
    {
        State.Counter++;
        State.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);
        await Task.CompletedTask;
    }
    
    public void RestoreState(TestAgentState savedState)
    {
        // Manually restore state (in real scenario, this would be handled by persistence)
        State.Name = savedState.Name;
        State.Counter = savedState.Counter;
        State.Items.Clear();
        State.Items.AddRange(savedState.Items);
        State.Metadata.Clear();
        foreach (var kvp in savedState.Metadata)
        {
            State.Metadata[kvp.Key] = kvp.Value;
        }
        State.LastUpdated = savedState.LastUpdated;
    }
    
    public async Task DeactivateAsync()
    {
        await OnDeactivateAsync();
    }
    
    public override string GetDescription()
    {
        return $"StatefulAgent: {State.Name} (Counter: {State.Counter})";
    }
}

#endregion
