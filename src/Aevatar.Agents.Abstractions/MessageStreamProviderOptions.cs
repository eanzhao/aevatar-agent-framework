namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Configuration options for selecting the message stream provider.
/// </summary>
public class MessageStreamProviderOptions
{
    /// <summary>
    /// The default provider to use (e.g., "Default", "MassTransit").
    /// </summary>
    public string Provider { get; set; } = "Default";

    /// <summary>
    /// Runtime-specific provider overrides.
    /// Key: Runtime Name (e.g., "Orleans", "Local"), Value: Provider Name.
    /// </summary>
    public Dictionary<string, string> Runtime { get; set; } = new();
}

