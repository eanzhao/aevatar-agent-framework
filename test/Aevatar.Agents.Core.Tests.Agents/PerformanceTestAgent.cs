using Aevatar.Agents.Abstractions.Attributes;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Core.Tests.Agents;

/// <summary>
/// Agent for performance testing with complex event handling
/// </summary>
public class PerformanceTestAgent : GAgentBase<ComplexAgentState>
{
    private readonly object _lock = new();
    public int EventsProcessed { get; private set; }
    public long TotalProcessingTimeMs { get; private set; }

    protected override Task OnActivateAsync(CancellationToken ct = default)
    {
        State.AgentId = Id.ToString();
        State.Status = ComplexAgentState.Types.Status.Active;
        State.CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
        return base.OnActivateAsync(ct);
    }

    [EventHandler(Priority = 1)]
    public async Task HandleComplexEvent(ComplexTestEvent evt)
    {
        var startTime = DateTime.UtcNow;

        // Simulate complex processing
        await Task.Yield();

        lock (_lock)
        {
            State.EventCount++;

            // Update nested state
            if (State.Nested == null)
            {
                State.Nested = new ComplexAgentState.Types.NestedState();
            }

            State.Nested.SubCounter++;
            State.Nested.SubTimestamp = Timestamp.FromDateTime(DateTime.UtcNow);

            // Add to collections
            foreach (var kvp in evt.Data)
            {
                State.DataMap[kvp.Key] = kvp.Value;
            }

            foreach (var nested in evt.NestedEvents)
            {
                State.NestedList.Add(new ComplexAgentState.Types.NestedState
                {
                    SubId = nested.NestedId,
                    SubCounter = nested.NestedValue,
                    SubTimestamp = Timestamp.FromDateTime(DateTime.UtcNow)
                });
            }

            // Track performance metrics
            EventsProcessed++;
            TotalProcessingTimeMs += (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
        }
    }

    [EventHandler(Priority = 2)]
    public async Task HandleBatchEvent(BatchTestEvent evt)
    {
        // Process batch of events
        foreach (var item in evt.Items)
        {
            State.EventCount++;
            State.DataMap[item.Key] = item.Value;
        }

        await Task.CompletedTask;
    }

    [AllEventHandler(Priority = 100)]
    public async Task HandleAllEvents(EventEnvelope envelope)
    {
        // Light processing for all events
        State.LastEventId = envelope.Id;
        State.LastUpdated = envelope.Timestamp ?? Timestamp.FromDateTime(DateTime.UtcNow);
        await Task.CompletedTask;
    }

    public override string GetDescription()
    {
        return $"PerformanceTestAgent: {State.AgentId} (Events: {State.EventCount})";
    }

    /// <summary>
    /// Reset performance counters
    /// </summary>
    public void ResetCounters()
    {
        EventsProcessed = 0;
        TotalProcessingTimeMs = 0;
    }

    /// <summary>
    /// Get average processing time in milliseconds
    /// </summary>
    public double GetAverageProcessingTimeMs()
    {
        return EventsProcessed > 0 ? TotalProcessingTimeMs / (double)EventsProcessed : 0;
    }
}