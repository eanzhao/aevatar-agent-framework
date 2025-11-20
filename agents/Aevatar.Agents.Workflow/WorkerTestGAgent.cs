using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Workflow;

/// <summary>
/// Worker Test Agent - A test agent for error handling scenarios in workflow
/// WorkerTestGAgent - 用于测试工作流中异常处理场景的工作节点代理
/// </summary>
public class WorkerTestGAgent : GAgentBase<WorkerTestGAgentState>
{
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Worker Test Agent {Id}: Test agent for error handling scenarios");
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        // Initialize state if needed
        if (string.IsNullOrEmpty(State.AgentId))
        {
            State.AgentId = Id.ToString();
            State.LastUpdated = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
        }
    }

    /// <summary>
    /// Handle SetInputEvent - Updates the input value
    /// </summary>
    [EventHandler]
    public async Task HandleSetInputEvent(SetInputEvent evt)
    {
        Logger.LogInformation("WorkerTestGAgent {Id} setting input to: {Input}", Id, evt.Input);

        // Update state
        State.Input = evt.Input;
        State.UpdateCount++;
        State.LastUpdated = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);

        Logger.LogInformation("WorkerTestGAgent {Id} input updated. Total updates: {Count}", Id, State.UpdateCount);
    }

    /// <summary>
    /// Handle SetFailureSummaryEvent - Sets failure summary for error simulation/testing
    /// </summary>
    [EventHandler]
    public async Task HandleSetFailureSummaryEvent(SetFailureSummaryEvent evt)
    {
        Logger.LogInformation("WorkerTestGAgent {Id} setting failure summary: {FailureSummary}", Id, evt.FailureSummary);

        State.FailureSummary = evt.FailureSummary;
        State.LastUpdated = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);

        Logger.LogInformation("WorkerTestGAgent {Id} failure summary updated", Id);
    }

    /// <summary>
    /// Handle WorkerTestConfiguration - Configuration event
    /// </summary>
    [EventHandler]
    public async Task HandleWorkerTestConfiguration(WorkerTestConfiguration configuration)
    {
        Logger.LogInformation("WorkerTestGAgent {Id} configuring with input: {Input}", Id, configuration.Input);

        // Set failure summary if provided
        if (!string.IsNullOrEmpty(configuration.FailureSummary))
        {
            var failureEvent = new SetFailureSummaryEvent
            {
                FailureSummary = configuration.FailureSummary
            };
            await HandleSetFailureSummaryEvent(failureEvent);
        }

        // Set member name if provided
        if (!string.IsNullOrEmpty(configuration.MemberName))
        {
            State.MemberName = configuration.MemberName;
        }

        // Create and handle SetInputEvent
        var setInputEvent = new SetInputEvent
        {
            Input = configuration.Input,
            Reason = string.IsNullOrEmpty(configuration.Description) ? "Configuration update" : configuration.Description
        };

        await HandleSetInputEvent(setInputEvent);
    }

    /// <summary>
    /// Configure the agent
    /// </summary>
    public async Task ConfigureAsync(WorkerTestConfiguration configuration, CancellationToken ct = default)
    {
        await HandleWorkerTestConfiguration(configuration);
    }

    /// <summary>
    /// Chat async - Returns configured input text as response
    /// If failure summary is set, throws an exception for testing error handling scenarios
    /// </summary>
    public Task<string> ChatAsync()
    {
        // Check for failure summary - throw exception if set (for testing error handling)
        if (!string.IsNullOrEmpty(State.FailureSummary))
        {
            Logger.LogWarning("WorkerTestGAgent {Id} throwing exception due to failure summary: {FailureSummary}", Id, State.FailureSummary);
            throw new InvalidOperationException(State.FailureSummary);
        }

        // Return configured input as response
        var response = string.IsNullOrEmpty(State.MemberName) 
            ? State.Input 
            : $"{State.MemberName} Send the message";

        Logger.LogInformation("WorkerTestGAgent {Id} ChatAsync returning: {Content}", Id, response);
        return Task.FromResult(response);
    }

    /// <summary>
    /// Get current input value
    /// </summary>
    public string GetInput() => State.Input;

    /// <summary>
    /// Get update count
    /// </summary>
    public int GetUpdateCount() => State.UpdateCount;

    /// <summary>
    /// Get failure summary
    /// </summary>
    public string GetFailureSummary() => State.FailureSummary;

    /// <summary>
    /// Get member name
    /// </summary>
    public string GetMemberName() => State.MemberName;
}

