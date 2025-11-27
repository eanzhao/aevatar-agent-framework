namespace Aevatar.Agents.Core.EventRouting;

/// <summary>
/// Configuration options for EventRouter.
/// Centralizes magic numbers to allow runtime configuration.
/// </summary>
public class EventRouterOptions
{
    /// <summary>
    /// Default maximum hop count for event propagation.
    /// Prevents infinite recursion in event chains.
    /// Default: 50
    /// </summary>
    public int DefaultMaxHopCount { get; set; } = 50;

    /// <summary>
    /// Safety maximum hop count - hard limit to prevent stack overflow.
    /// Events exceeding this count are forcefully stopped regardless of other settings.
    /// Should be higher than DefaultMaxHopCount to allow some flexibility.
    /// Default: 100
    /// </summary>
    public int SafetyMaxHopCount { get; set; } = 100;

    /// <summary>
    /// Default minimum hop count before event can be processed by handlers.
    /// -1 means no minimum (event is processed immediately).
    /// Default: -1
    /// </summary>
    public int DefaultMinHopCount { get; set; } = -1;

    /// <summary>
    /// Creates default options.
    /// </summary>
    public static EventRouterOptions Default => new();

    /// <summary>
    /// Validates the options and throws if invalid.
    /// </summary>
    public void Validate()
    {
        if (DefaultMaxHopCount <= 0)
            throw new ArgumentException("DefaultMaxHopCount must be positive", nameof(DefaultMaxHopCount));

        if (SafetyMaxHopCount <= DefaultMaxHopCount)
            throw new ArgumentException(
                "SafetyMaxHopCount must be greater than DefaultMaxHopCount",
                nameof(SafetyMaxHopCount));

        if (DefaultMinHopCount < -1)
            throw new ArgumentException(
                "DefaultMinHopCount must be -1 or greater",
                nameof(DefaultMinHopCount));
    }
}

