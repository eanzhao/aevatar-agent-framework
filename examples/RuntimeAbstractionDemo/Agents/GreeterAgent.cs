using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace RuntimeAbstractionDemo.Agents;

/// <summary>
/// A simple greeter agent that demonstrates runtime abstraction.
/// This agent can run on any runtime (Local, Orleans, ProtoActor).
/// </summary>
public class GreeterAgent : GAgentBase<DemoAgentState>
{
    // Parameterless constructor required for framework
    public GreeterAgent() : base()
    {
    }
    
    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        
        // Initialize state during activation
        State.Name = $"Greeter_{Id.ToString("N").Substring(0, 8)}";
        State.MessageCount = 0;
        State.LastUpdate = Timestamp.FromDateTime(DateTime.UtcNow);
        State.RuntimeType = "Unknown";
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Greeter Agent: {State.Name}");
    }

    [EventHandler]
    public async Task HandleHelloEvent(HelloEvent evt)
    {
        Logger.LogInformation("[{Name}] Received hello from {Sender}: {Message}",
            State.Name, evt.Sender, evt.Message);
        
        State.MessageCount++;
        State.LastUpdate = Timestamp.FromDateTime(DateTime.UtcNow);

        // Send a response back
        var response = new HelloEvent
        {
            Sender = State.Name,
            Message = $"Hello back! I've received {State.MessageCount} messages so far.",
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        await PublishAsync(response, EventDirection.Up);
    }

    [EventHandler]
    public async Task HandleWorkRequest(WorkRequestEvent evt)
    {
        Logger.LogInformation("[{Name}] Received work request: {TaskId} - {Description}",
            State.Name, evt.TaskId, evt.Description);
        
        // Simulate doing work
        await Task.Delay(Random.Shared.Next(100, 500));
        
        // Send completion event
        var completed = new WorkCompletedEvent
        {
            TaskId = evt.TaskId,
            WorkerId = State.Name,
            Success = true,
            Result = $"Task {evt.TaskId} completed by {State.Name}"
        };

        await PublishAsync(completed, EventDirection.Up);
    }

    public void SetRuntimeType(string runtimeType)
    {
        State.RuntimeType = runtimeType;
        Logger.LogInformation("[{Name}] Runtime type set to: {Runtime}", 
            State.Name, runtimeType);
    }
}
