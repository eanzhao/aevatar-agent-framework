using System;
using System.Threading.Tasks;
using Aevatar.Agents.Core;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Google.Protobuf.WellKnownTypes;
using Business.Server;
using Microsoft.Extensions.Logging;

namespace Aevatar.App.Agents.Agents;

public class SimpleBusinessAgent : GAgentBase<SimpleBusinessAgentState>
{
    public SimpleBusinessAgent()
    {
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Simple Business Agent {Id} - Processed: {State.ProcessedCount}");
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        Logger.LogInformation("🌟 Agent {AgentId} Activated", Id);
        
        if (State.LastUpdated == null)
        {
            State.AgentId = Id.ToString();
            State.ProcessedCount = 0;
            State.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);
        }
    }

    [EventHandler]
    public async Task HandleBusinessMessage(BusinessMessageEvent evt)
    {
        Logger.LogInformation("📨 Agent {AgentId} received: {Message}", Id, evt.Message);
        
        State.ProcessedCount++;
        State.LastMessage = evt.Message;
        State.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);
        
        // Publish an event to the stream (optional)
        await PublishAsync(evt);
    }

    public async Task<string> ProcessMessageAsync(string message)
    {
        var evt = new BusinessMessageEvent
        {
            Message = message,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        await HandleBusinessMessage(evt);
        return $"Processed: {message} (Total: {State.ProcessedCount})";
    }

    public Task<SimpleBusinessAgentState> GetStatisticsAsync()
    {
        return Task.FromResult(State);
    }
}

