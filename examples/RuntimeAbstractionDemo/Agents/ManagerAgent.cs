using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace RuntimeAbstractionDemo.Agents;

/// <summary>
/// A manager agent that coordinates multiple worker agents.
/// Demonstrates parent-child relationships across different runtimes.
/// </summary>
public class ManagerAgent : GAgentBase<DemoAgentState>
{
    private readonly List<string> _completedTasks = new();
    private readonly Dictionary<string, DateTime> _pendingTasks = new();

    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        
        // Initialize state during activation
        State.Name = $"Manager_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        State.MessageCount = 0;
        State.LastUpdate = Timestamp.FromDateTime(DateTime.UtcNow);
        State.RuntimeType = "Unknown";
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Manager Agent: {State.Name} - Managing {_completedTasks.Count + _pendingTasks.Count} tasks");
    }

    /// <summary>
    /// Distributes work to child agents.
    /// </summary>
    public async Task DistributeWork(string taskDescription, int taskCount = 3)
    {
        Logger.LogInformation("[{Name}] Distributing {Count} tasks to workers", 
            State.Name, taskCount);

        for (int i = 0; i < taskCount; i++)
        {
            var taskId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var workRequest = new WorkRequestEvent
            {
                TaskId = taskId,
                Description = $"{taskDescription} - Part {i + 1}",
                Priority = i
            };

            _pendingTasks[taskId] = DateTime.UtcNow;
            
            // Send to children (workers)
            await PublishAsync(workRequest);
        }
    }

    [EventHandler]
    public async Task HandleWorkCompleted(WorkCompletedEvent evt)
    {
        Logger.LogInformation("[{Name}] Work completed: {TaskId} by {Worker} - Success: {Success}",
            State.Name, evt.TaskId, evt.WorkerId, evt.Success);
        
        if (_pendingTasks.ContainsKey(evt.TaskId))
        {
            _pendingTasks.Remove(evt.TaskId);
            _completedTasks.Add(evt.TaskId);
            
            State.MessageCount++;
            State.LastUpdate = Timestamp.FromDateTime(DateTime.UtcNow);
        }

        // Report progress
        Logger.LogInformation("[{Name}] Progress: {Completed}/{Total} tasks completed",
            State.Name, _completedTasks.Count, _completedTasks.Count + _pendingTasks.Count);

        if (_pendingTasks.Count == 0 && _completedTasks.Count > 0)
        {
            Logger.LogInformation("[{Name}] All tasks completed! Total: {Count}",
                State.Name, _completedTasks.Count);
            
            // Notify parent if any
            var notification = new HelloEvent
            {
                Sender = State.Name,
                Message = $"All {_completedTasks.Count} tasks completed successfully!",
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
            
            await PublishAsync(notification, EventDirection.Up);
        }
    }

    [EventHandler]
    public async Task HandleHelloFromChild(HelloEvent evt)
    {
        Logger.LogInformation("[{Name}] Received hello from child {Sender}: {Message}",
            State.Name, evt.Sender, evt.Message);
        
        State.MessageCount++;
        await Task.CompletedTask;
    }

    public void SetRuntimeType(string runtimeType)
    {
        State.RuntimeType = runtimeType;
        Logger.LogInformation("[{Name}] Runtime type set to: {Runtime}", 
            State.Name, runtimeType);
    }

    public int GetCompletedTaskCount() => _completedTasks.Count;
    public int GetPendingTaskCount() => _pendingTasks.Count;
}
