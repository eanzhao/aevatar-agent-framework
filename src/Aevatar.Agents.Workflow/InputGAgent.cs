using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Workflow;

/// <summary>
/// Input Agent - Returns configured input text as response
/// InputGAgent - 返回配置的输入文本作为响应
/// </summary>
public class InputGAgent : GAgentBase<InputGAgentState, SetInputEvent, InputConfiguration>
{
    public InputGAgent() : base()
    {
    }

    public InputGAgent(Guid id, ILogger<InputGAgent>? logger = null) : base(id, logger)
    {
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Input Agent {Id}: Returns configured input text (Current: {State.Input})");
    }

    public override async Task OnActivateAsync(CancellationToken ct = default)
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
        Logger.LogInformation("InputGAgent {Id} setting input to: {Input}", Id, evt.Input);

        // Update state
        State.Input = evt.Input;
        State.UpdateCount++;
        State.LastUpdated = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);

        // Publish event to notify others (optional)
        await PublishAsync(evt, EventDirection.Down, ct: default);

        Logger.LogInformation("InputGAgent {Id} input updated. Total updates: {Count}", Id, State.UpdateCount);
    }

    /// <summary>
    /// Configuration handler - Called when agent is configured
    /// </summary>
    protected override async Task OnConfigureAsync(InputConfiguration configuration, CancellationToken ct = default)
    {
        Logger.LogInformation("InputGAgent {Id} configuring with input: {Input}", Id, configuration.Input);

        // Create and handle SetInputEvent
        var setInputEvent = new SetInputEvent
        {
            Input = configuration.Input,
            Reason = string.IsNullOrEmpty(configuration.Description) ? "Configuration update" : configuration.Description
        };

        await HandleSetInputEvent(setInputEvent);
    }

    /// <summary>
    /// Get current input value
    /// </summary>
    public string GetInput() => State.Input;

    /// <summary>
    /// Get update count
    /// </summary>
    public int GetUpdateCount() => State.UpdateCount;
}
