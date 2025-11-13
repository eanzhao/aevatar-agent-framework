namespace Aevatar.Agents.Abstractions.Attributes;

/// <summary>
/// Marking event handler method.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class EventHandlerAttribute : Attribute
{
    /// <summary>
    /// Whether handle events published by current agent itself.
    /// </summary>
    public bool AllowSelfHandling { get; set; }

    /// <summary>
    /// Handler priority (The smaller the number, the higher the priority)
    /// </summary>
    public int Priority { get; set; } = 0;
}