using Aevatar.Agents.Abstractions.Attributes;
using Google.Protobuf;

namespace Aevatar.Agents.Core.Tests.Agents;

/// <summary>
/// Real parent agent that uses PublishAsync for communication
/// </summary>
public class RealParentAgent : GAgentBase<TestAgentState>
{
    public int ReceivedFromChildrenCount { get; private set; }
    public string? LastReceivedEventId { get; private set; }
    
    public override string GetDescription() => "RealParentAgent";
    
    /// <summary>
    /// Broadcast event DOWN to children
    /// </summary>
    public async Task BroadcastToChildren<TEvent>(TEvent evt) 
        where TEvent : IMessage
    {
        await PublishAsync(evt, EventDirection.Down);
    }
    
    [EventHandler]
    public async Task HandleChildEvent(TestEvent evt)
    {
        ReceivedFromChildrenCount++;
        LastReceivedEventId = evt.EventId;
        await Task.CompletedTask;
    }
}