using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Workflow;

/// <summary>
/// Input Agent - Returns configured input text as response
/// InputGAgent - 返回配置的输入文本作为响应
/// </summary>
public class InputGAgent : GAgentBase<InputGAgentState, InputConfiguration>
{
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Input Agent {Id}: Returns configured input text (Current: {State.Input})");
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

        // Apply configuration to state if config is set
        if (Config != null && !string.IsNullOrEmpty(Config.Input))
        {
            State.Input = Config.Input;
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

        // Update config if needed
        if (Config != null)
        {
            Config.Input = evt.Input;
            Config.Description = evt.Reason;
        }

        // Publish event to notify others (optional)
        await PublishAsync(evt, EventDirection.Down, ct: default);

        Logger.LogInformation("InputGAgent {Id} input updated. Total updates: {Count}", Id, State.UpdateCount);
    }

    /// <summary>
    /// Handle InputConfiguration - Configuration event
    /// </summary>
    [EventHandler]
    public async Task HandleInputConfiguration(InputConfiguration configuration)
    {
        Logger.LogInformation("InputGAgent {Id} configuring with input: {Input}", Id, configuration.Input);

        // Update config
        Config.Input = configuration.Input;
        Config.Description = configuration.Description;

        // Create and handle SetInputEvent
        var setInputEvent = new SetInputEvent
        {
            Input = configuration.Input,
            Reason = string.IsNullOrEmpty(configuration.Description)
                ? "Configuration update"
                : configuration.Description
        };

        await HandleSetInputEvent(setInputEvent);
    }

    /// <summary>
    /// Configure the agent
    /// </summary>
    public async Task ConfigureAsync(InputConfiguration configuration, CancellationToken ct = default)
    {
        await HandleInputConfiguration(configuration);
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
