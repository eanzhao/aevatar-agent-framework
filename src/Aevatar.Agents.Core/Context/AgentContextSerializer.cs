using System.Globalization;
using System.Runtime.CompilerServices;
using Aevatar.Agents.Abstractions.Context;

namespace Aevatar.Agents.Core.Context;

/// <summary>
/// Serializes/deserializes agent context to/from EventEnvelope metadata.
/// Uses type-prefixed string encoding for common types.
/// Optimized for minimal allocations on hot paths.
/// </summary>
public static class AgentContextSerializer
{
    // Type prefixes for serialization (2 chars including colon)
    private const char StringPrefix = 's';
    private const char BoolPrefix = 'b';
    private const char IntPrefix = 'i';
    private const char LongPrefix = 'l';
    private const char DoublePrefix = 'd';
    private const char GuidPrefix = 'g';
    private const string DateTimePrefix = "dt";

    /// <summary>
    /// Serialize context to string dictionary for EventEnvelope.
    /// Uses options to filter keys and enforce limits.
    /// </summary>
    public static IDictionary<string, string> Serialize(
        IAgentContext context,
        AgentContextPropagationOptions? options = null)
    {
        options ??= AgentContextPropagationOptions.Default;
        var result = new Dictionary<string, string>();
        var totalBytes = 0;

        foreach (var (key, value) in context.GetAll())
        {
            // Check if key should be propagated
            if (!options.ShouldPropagate(key))
                continue;

            if (value == null)
                continue;

            // Check key limit
            if (result.Count >= options.MaxKeys)
                break;

            var serialized = SerializeValue(value);

            // Check size limit
            var entrySize = key.Length + serialized.Length;
            if (totalBytes + entrySize > options.MaxTotalBytes)
                break;

            result[key] = serialized;
            totalBytes += entrySize;
        }

        return result;
    }

    /// <summary>
    /// Deserialize context from EventEnvelope metadata.
    /// Uses options to filter keys.
    /// </summary>
    public static void Deserialize(
        IDictionary<string, string> metadata,
        IAgentContext context,
        AgentContextPropagationOptions? options = null)
    {
        options ??= AgentContextPropagationOptions.Default;

        foreach (var (key, value) in metadata)
        {
            // Check if key should be accepted
            if (!options.ShouldPropagate(key))
                continue;

            context.Set(key, DeserializeValue(value));
        }
    }

    /// <summary>
    /// Serialize a single value to string with type prefix.
    /// Optimized to use string.Concat for reduced allocations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string SerializeValue(object value)
    {
        return value switch
        {
            string s => string.Concat(StringPrefix, ":", s),
            bool b => string.Concat(BoolPrefix, ":", b ? "True" : "False"),
            int i => string.Concat(IntPrefix, ":", i.ToString(CultureInfo.InvariantCulture)),
            long l => string.Concat(LongPrefix, ":", l.ToString(CultureInfo.InvariantCulture)),
            double dbl => string.Concat(DoublePrefix, ":", dbl.ToString(CultureInfo.InvariantCulture)),
            DateTime dt => string.Concat(DateTimePrefix, ":", dt.ToString("O", CultureInfo.InvariantCulture)),
            Guid g => string.Concat(GuidPrefix, ":", g.ToString("D")),
            _ => string.Concat(StringPrefix, ":", value.ToString() ?? string.Empty)
        };
    }

    /// <summary>
    /// Deserialize a single value from string with type prefix.
    /// Optimized with span-based parsing for reduced allocations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static object? DeserializeValue(string serialized)
    {
        // Null or empty string - return as-is
        if (string.IsNullOrEmpty(serialized))
            return serialized;

        // Minimum valid format is "X:" (length >= 2)
        if (serialized.Length < 2)
            return serialized;

        var firstChar = serialized[0];
        
        // Handle datetime prefix "dt:" first (before checking single-char prefix format)
        if (firstChar == 'd' && serialized.Length >= 3 && serialized[1] == 't' && serialized[2] == ':')
        {
            var dtValue = serialized.AsSpan(3);
            return DateTime.TryParse(dtValue, CultureInfo.InvariantCulture, 
                DateTimeStyles.RoundtripKind, out var dt) ? dt : serialized;
        }

        // Single-char prefix format: "X:value" (second char must be ':')
        if (serialized[1] != ':')
            return serialized;

        // Value starts at index 2 (may be empty for "s:")
        var valueSpan = serialized.AsSpan(2);

        return firstChar switch
        {
            StringPrefix => serialized[2..], // Returns "" for "s:", "hello" for "s:hello"
            BoolPrefix => ParseBool(valueSpan, serialized),
            IntPrefix => ParseInt(valueSpan, serialized),
            LongPrefix => ParseLong(valueSpan, serialized),
            DoublePrefix => ParseDouble(valueSpan, serialized),
            GuidPrefix => ParseGuid(valueSpan, serialized),
            _ => serialized
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static object ParseBool(ReadOnlySpan<char> value, string fallback)
    {
        // bool.TryParse supports Span<char> without allocations
        return bool.TryParse(value, out var result) ? result : fallback;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static object ParseInt(ReadOnlySpan<char> value, string fallback)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) 
            ? i : fallback;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static object ParseLong(ReadOnlySpan<char> value, string fallback)
    {
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) 
            ? l : fallback;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static object ParseDouble(ReadOnlySpan<char> value, string fallback)
    {
        return double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, 
            CultureInfo.InvariantCulture, out var d) ? d : fallback;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static object ParseGuid(ReadOnlySpan<char> value, string fallback)
    {
        return Guid.TryParse(value, out var g) ? g : fallback;
    }
}
