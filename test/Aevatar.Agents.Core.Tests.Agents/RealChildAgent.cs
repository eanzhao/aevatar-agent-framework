using Aevatar.Agents.Abstractions.Attributes;
using Google.Protobuf;

namespace Aevatar.Agents.Core.Tests.Agents;

/// <summary>
/// Real child agent that uses PublishAsync for communication
/// </summary>
public class RealChildAgent : GAgentBase<TestAgentState>
{
    public int ReceivedFromParentCount { get; private set; }
    public string? LastReceivedCommandId { get; private set; }

    public override string GetDescription() => "RealChildAgent";

    /// <summary>
    /// Send event UP to parent
    /// </summary>
    public async Task SendEventToParent<TEvent>(TEvent evt)
        where TEvent : IMessage
    {
        await PublishAsync(evt, EventDirection.Up);
    }

    /// <summary>
    /// Broadcast to both parent and children
    /// </summary>
    public async Task BroadcastToAll<TEvent>(TEvent evt)
        where TEvent : IMessage
    {
        await PublishAsync(evt, EventDirection.Both);
    }

    [EventHandler]
    public async Task HandleCommand(TestCommand cmd)
    {
        ReceivedFromParentCount++;
        LastReceivedCommandId = cmd.CommandId;
        await Task.CompletedTask;
    }

    public string? LastReceivedEventId { get; private set; }

    [EventHandler]
    public async Task HandleEvent(TestEvent evt)
    {
        ReceivedFromParentCount++;
        LastReceivedEventId = evt.EventId;
        await Task.CompletedTask;
    }
}