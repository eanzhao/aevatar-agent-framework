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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IDictionary<string, string> Serialize(IAgentContext context)
    {
        var result = new Dictionary<string, string>(AgentContextKeys.AllowedPropagationKeys.Count);
        var totalBytes = 0;

        foreach (var (key, value) in context.GetAll())
        {
            // Only serialize allowlisted keys - HashSet.Contains is O(1)
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        if (string.IsNullOrEmpty(serialized) || serialized.Length < 2)
            return serialized;

        // Fast path: check first character for single-char prefixes
        var firstChar = serialized[0];
        
        // Handle datetime prefix (2 chars)
        if (firstChar == 'd' && serialized.Length > 3 && serialized[1] == 't' && serialized[2] == ':')
        {
            var dtValue = serialized.AsSpan(3);
            return DateTime.TryParse(dtValue, CultureInfo.InvariantCulture, 
                DateTimeStyles.RoundtripKind, out var dt) ? dt : serialized;
        }

        // Single char prefix requires at least "X:Y"
        if (serialized.Length < 3 || serialized[1] != ':')
            return serialized;

        var valueSpan = serialized.AsSpan(2);

        return firstChar switch
        {
            StringPrefix => serialized[2..], // Return substring for string type
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
        if (value.Length == 4 && 
            (value[0] == 'T' || value[0] == 't') &&
            (value[1] == 'r' || value[1] == 'R') &&
            (value[2] == 'u' || value[2] == 'U') &&
            (value[3] == 'e' || value[3] == 'E'))
            return true;
        
        if (value.Length == 5 &&
            (value[0] == 'F' || value[0] == 'f') &&
            (value[1] == 'a' || value[1] == 'A') &&
            (value[2] == 'l' || value[2] == 'L') &&
            (value[3] == 's' || value[3] == 'S') &&
            (value[4] == 'e' || value[4] == 'E'))
            return false;
        
        return fallback;
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
