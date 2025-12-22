using System.Globalization;
using Aevatar.Agents.Abstractions.Context;

namespace Aevatar.Agents.Core.Context;

/// <summary>
/// Serializes/deserializes agent context to/from EventEnvelope metadata.
/// Uses type-prefixed string encoding for common types.
/// </summary>
public static class AgentContextSerializer
{
    // Type prefixes for serialization
    private const string StringPrefix = "s:";
    private const string BoolPrefix = "b:";
    private const string IntPrefix = "i:";
    private const string LongPrefix = "l:";
    private const string DoublePrefix = "d:";
    private const string DateTimePrefix = "dt:";
    private const string GuidPrefix = "g:";

    /// <summary>
    /// Maximum number of keys allowed in context metadata.
    /// </summary>
    public const int MaxKeys = 16;

    /// <summary>
    /// Maximum total serialized bytes allowed.
    /// </summary>
    public const int MaxTotalBytes = 4096;

    /// <summary>
    /// Serialize context to string dictionary for EventEnvelope.
    /// Only allowlisted keys are serialized.
    /// </summary>
    public static IDictionary<string, string> Serialize(IAgentContext context)
    {
        var result = new Dictionary<string, string>();
        var totalBytes = 0;

        foreach (var (key, value) in context.GetAll())
        {
            // Only serialize allowlisted keys
            if (!AgentContextKeys.AllowedPropagationKeys.Contains(key))
                continue;

            if (value == null)
                continue;

            // Check key limit
            if (result.Count >= MaxKeys)
                break;

            var serialized = SerializeValue(value);

            // Check size limit
            var entrySize = key.Length + serialized.Length;
            if (totalBytes + entrySize > MaxTotalBytes)
                break;

            result[key] = serialized;
            totalBytes += entrySize;
        }

        return result;
    }

    /// <summary>
    /// Deserialize context from EventEnvelope metadata.
    /// </summary>
    public static void Deserialize(
        IDictionary<string, string> metadata,
        IAgentContext context)
    {
        foreach (var (key, value) in metadata)
        {
            // Only deserialize allowlisted keys
            if (!AgentContextKeys.AllowedPropagationKeys.Contains(key))
                continue;

            context.Set(key, DeserializeValue(value));
        }
    }

    /// <summary>
    /// Serialize a single value to string with type prefix.
    /// </summary>
    private static string SerializeValue(object value)
    {
        return value switch
        {
            string s => $"{StringPrefix}{s}",
            bool b => $"{BoolPrefix}{b}",
            int i => $"{IntPrefix}{i}",
            long l => $"{LongPrefix}{l}",
            double dbl => $"{DoublePrefix}{dbl.ToString(CultureInfo.InvariantCulture)}",
            DateTime dt => $"{DateTimePrefix}{dt:O}",
            Guid g => $"{GuidPrefix}{g}",
            _ => $"{StringPrefix}{value}" // Fallback to string representation
        };
    }

    /// <summary>
    /// Deserialize a single value from string with type prefix.
    /// </summary>
    private static object? DeserializeValue(string serialized)
    {
        if (string.IsNullOrEmpty(serialized))
            return null;

        // Find the prefix separator
        var colonIndex = serialized.IndexOf(':');
        if (colonIndex < 1 || colonIndex >= serialized.Length - 1)
            return serialized;

        var prefix = serialized[..(colonIndex + 1)];
        var value = serialized[(colonIndex + 1)..];

        return prefix switch
        {
            StringPrefix => value,
            BoolPrefix => bool.TryParse(value, out var b) ? b : value,
            IntPrefix => int.TryParse(value, out var i) ? i : value,
            LongPrefix => long.TryParse(value, out var l) ? l : value,
            DoublePrefix => double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : value,
            DateTimePrefix => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? dt : value,
            GuidPrefix => Guid.TryParse(value, out var g) ? g : value,
            _ => serialized
        };
    }
}

