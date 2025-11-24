using System.Diagnostics.Metrics;

namespace Aevatar.Agents.Core.Observability;

/// <summary>
/// Performance metrics for Agent framework
/// </summary>
public static class AgentMetrics
{
    private static readonly Meter Meter = new("Aevatar.Agents", "1.0.0");

    // Counters
    public static readonly Counter<long> EventsPublished = Meter.CreateCounter<long>(
        "aevatar.agents.events.published",
        description: "Total number of events published");

    public static readonly Counter<long> EventsHandled = Meter.CreateCounter<long>(
        "aevatar.agents.events.handled",
        description: "Total number of events handled");

    public static readonly Counter<long> EventsDropped = Meter.CreateCounter<long>(
        "aevatar.agents.events.dropped",
        description: "Total number of events dropped");

    public static readonly Counter<long> ExceptionsOccurred = Meter.CreateCounter<long>(
        "aevatar.agents.exceptions",
        description: "Total number of exceptions occurred");

    // Histograms (Latency)
    public static readonly Histogram<double> EventHandlingLatency = Meter.CreateHistogram<double>(
        "aevatar.agents.event.handling.duration",
        unit: "ms",
        description: "Event handling duration in milliseconds");

    public static readonly Histogram<double> EventPublishLatency = Meter.CreateHistogram<double>(
        "aevatar.agents.event.publish.duration",
        unit: "ms",
        description: "Event publish duration in milliseconds");

    // ObservableGauge requires a callback function
    private static int _activeActorCount = 0;
    private static int _queueLength = 0;

    public static readonly ObservableGauge<int> ActiveActors = Meter.CreateObservableGauge<int>(
        "aevatar.agents.active.count",
        () => _activeActorCount,
        description: "Number of active actors");

    public static readonly ObservableGauge<int> QueueLength = Meter.CreateObservableGauge<int>(
        "aevatar.agents.queue.length",
        () => _queueLength,
        description: "Current queue length");

    /// <summary>
    /// Update active Actor count
    /// </summary>
    public static void UpdateActiveActorCount(int count)
    {
        _activeActorCount = count;
    }

    /// <summary>
    /// Update queue length
    /// </summary>
    public static void UpdateQueueLength(int length)
    {
        _queueLength = length;
    }

    /// <summary>
    /// Record event published
    /// </summary>
    public static void RecordEventPublished(string eventType, string agentId)
    {
        EventsPublished.Add(1, new KeyValuePair<string, object?>("event.type", eventType),
            new KeyValuePair<string, object?>("agent.id", agentId));
    }

    /// <summary>
    /// Record event handled
    /// </summary>
    public static void RecordEventHandled(string eventType, string agentId, double latencyMs)
    {
        EventsHandled.Add(1, new KeyValuePair<string, object?>("event.type", eventType),
            new KeyValuePair<string, object?>("agent.id", agentId));

        EventHandlingLatency.Record(latencyMs,
            new KeyValuePair<string, object?>("event.type", eventType),
            new KeyValuePair<string, object?>("agent.id", agentId));
    }

    /// <summary>
    /// Record exception
    /// </summary>
    public static void RecordException(string exceptionType, string agentId, string operation)
    {
        ExceptionsOccurred.Add(1,
            new KeyValuePair<string, object?>("exception.type", exceptionType),
            new KeyValuePair<string, object?>("agent.id", agentId),
            new KeyValuePair<string, object?>("operation", operation));
    }
}