using System;

namespace Aevatar.Agents.AI;

/// <summary>
/// Partial class extensions for ChatRequest protobuf message
/// </summary>
public partial class ChatRequest
{
    /// <summary>
    /// Creates a new ChatRequest with a generated ID
    /// </summary>
    /// <param name="message">The message content</param>
    /// <returns>A new ChatRequest instance</returns>
    public static ChatRequest Create(string message)
    {
        return new ChatRequest
        {
            Message = message,
            RequestId = Guid.NewGuid().ToString()
        };
    }

    /// <summary>
    /// Adds or updates a context value
    /// </summary>
    /// <param name="key">Context key</param>
    /// <param name="value">Context value</param>
    public void AddContext(string key, string value)
    {
        Context[key] = value;
    }

    /// <summary>
    /// Sets temperature if not already set
    /// </summary>
    /// <param name="temperature">Temperature value (0-1)</param>
    public void SetTemperatureIfNotSet(double temperature)
    {
        if (Temperature == 0)
        {
            Temperature = temperature;
        }
    }

    /// <summary>
    /// Sets max tokens if not already set
    /// </summary>
    /// <param name="maxTokens">Maximum tokens</param>
    public void SetMaxTokensIfNotSet(int maxTokens)
    {
        if (MaxTokens == 0)
        {
            MaxTokens = maxTokens;
        }
    }
}
