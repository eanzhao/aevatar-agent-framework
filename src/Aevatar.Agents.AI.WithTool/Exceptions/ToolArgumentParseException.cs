using System.Text.Json;

namespace Aevatar.Agents.AI.WithTool.Exceptions;

// ============================================================
//  Tool Argument Parse Exception
//  Thrown when tool arguments cannot be parsed from JSON
// ============================================================

/// <summary>
/// Exception thrown when tool arguments fail to parse
/// </summary>
public class ToolArgumentParseException : Exception
{
    /// <summary>
    /// Raw JSON that failed to parse
    /// </summary>
    public string RawJson { get; }

    /// <summary>
    /// Tool name (if available)
    /// </summary>
    public string? ToolName { get; }

    /// <summary>
    /// Creates a new instance
    /// </summary>
    /// <param name="rawJson">Raw JSON that failed to parse</param>
    /// <param name="inner">Inner exception</param>
    public ToolArgumentParseException(string rawJson, Exception inner)
        : base($"Failed to parse tool arguments: {TruncateJson(rawJson)}", inner)
    {
        RawJson = rawJson;
    }

    /// <summary>
    /// Creates a new instance with tool name
    /// </summary>
    /// <param name="toolName">Tool name</param>
    /// <param name="rawJson">Raw JSON that failed to parse</param>
    /// <param name="inner">Inner exception</param>
    public ToolArgumentParseException(string toolName, string rawJson, Exception inner)
        : base($"Failed to parse arguments for tool '{toolName}': {TruncateJson(rawJson)}", inner)
    {
        ToolName = toolName;
        RawJson = rawJson;
    }


    private static string TruncateJson(string json)
    {
        const int maxLength = 200;
        if (string.IsNullOrEmpty(json)) return "(empty)";
        return json.Length > maxLength ? json[..maxLength] + "..." : json;
    }
}

/// <summary>
/// Result of tool argument parsing
/// </summary>
public readonly record struct ToolArgumentParseResult
{
    /// <summary>
    /// Whether parsing was successful
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Parsed parameters (empty if failed)
    /// </summary>
    public Dictionary<string, object> Parameters { get; init; }

    /// <summary>
    /// Error message if parsing failed
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Creates a successful result
    /// </summary>
    public static ToolArgumentParseResult Ok(Dictionary<string, object> parameters)
        => new() { Success = true, Parameters = parameters };

    /// <summary>
    /// Creates a failed result
    /// </summary>
    public static ToolArgumentParseResult Fail(string error)
        => new() { Success = false, Parameters = new Dictionary<string, object>(), Error = error };
}

