using System;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Microsoft.Extensions.Logging;

namespace StateStoreDemo;

/// <summary>
/// Counter Agent - demonstrates automatic state persistence
/// </summary>
public class CounterAgent : GAgentBase<CounterState>
{
    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleIncrementAsync(IncrementEvent evt)
    {
        // State is automatically loaded before this handler
        State.Count += evt.Amount;
        State.LastEvent = $"Incremented by {evt.Amount}";

        Console.WriteLine($"[{Id}] State updated: Count = {State.Count}");

        // State is automatically saved after this handler
        await Task.CompletedTask;
    }

    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleGetCountAsync(GetCountQuery query)
    {
        Console.WriteLine($"[{Id}] Current count: {State.Count}, Last event: {State.LastEvent}");
        await Task.CompletedTask;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Counter agent. Current count: {State.Count}");
    }
}