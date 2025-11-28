using System.Text.Json;

namespace Aevatar.Agents.AI.Core.Utils;

// ============================================================
//  LLM Response Parser
//  Robust JSON extraction from LLM responses
//  Handles markdown code blocks, partial JSON, etc.
// ============================================================

/// <summary>
/// Utility class for parsing LLM responses containing JSON.
/// Handles common LLM output patterns like markdown code blocks.
/// </summary>
public static class LLMResponseParser
{
    /// <summary>
    /// Extract JSON from LLM response (handles markdown code blocks).
    /// </summary>
    /// <param name="content">Raw LLM response content</param>
    /// <returns>Extracted JSON string</returns>
    public static string ExtractJson(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "{}";

        var trimmed = content.Trim();

        // Handle markdown code blocks: ```json ... ``` or ``` ... ```
        if (trimmed.StartsWith("```"))
        {
            return ExtractFromMarkdownBlock(trimmed);
        }

        // Handle inline JSON (starts with { or [)
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            return ExtractBracketedContent(trimmed);
        }

        // Try to find JSON anywhere in the content
        return FindJsonInContent(trimmed);
    }

    /// <summary>
    /// Extract JSON from markdown code block.
    /// </summary>
    private static string ExtractFromMarkdownBlock(string content)
    {
        // Find first { or [ (start of JSON)
        var objectStart = content.IndexOf('{');
        var arrayStart = content.IndexOf('[');

        int start;
        char closingChar;

        if (objectStart >= 0 && (arrayStart < 0 || objectStart < arrayStart))
        {
            start = objectStart;
            closingChar = '}';
        }
        else if (arrayStart >= 0)
        {
            start = arrayStart;
            closingChar = ']';
        }
        else
        {
            // No JSON found in code block
            return "{}";
        }

        // Find matching closing bracket
        var end = FindMatchingBracket(content, start, closingChar);

        if (end > start)
        {
            return content.Substring(start, end - start + 1);
        }

        // Fallback: find last occurrence of closing char
        end = content.LastIndexOf(closingChar);
        if (end > start)
        {
            return content.Substring(start, end - start + 1);
        }

        return "{}";
    }

    /// <summary>
    /// Extract content within brackets.
    /// </summary>
    private static string ExtractBracketedContent(string content)
    {
        var openChar = content[0];
        var closeChar = openChar == '{' ? '}' : ']';

        var end = FindMatchingBracket(content, 0, closeChar);
        if (end > 0)
        {
            return content[..(end + 1)];
        }

        // Fallback: find last closing bracket
        end = content.LastIndexOf(closeChar);
        if (end > 0)
        {
            return content[..(end + 1)];
        }

        return content;
    }

    /// <summary>
    /// Find JSON anywhere in content.
    /// </summary>
    private static string FindJsonInContent(string content)
    {
        // Try to find { ... }
        var objectStart = content.IndexOf('{');
        if (objectStart >= 0)
        {
            var end = FindMatchingBracket(content, objectStart, '}');
            if (end > objectStart)
            {
                return content.Substring(objectStart, end - objectStart + 1);
            }

            // Fallback
            end = content.LastIndexOf('}');
            if (end > objectStart)
            {
                return content.Substring(objectStart, end - objectStart + 1);
            }
        }

        // Try to find [ ... ]
        var arrayStart = content.IndexOf('[');
        if (arrayStart >= 0)
        {
            var end = FindMatchingBracket(content, arrayStart, ']');
            if (end > arrayStart)
            {
                return content.Substring(arrayStart, end - arrayStart + 1);
            }

            // Fallback
            end = content.LastIndexOf(']');
            if (end > arrayStart)
            {
                return content.Substring(arrayStart, end - arrayStart + 1);
            }
        }

        // No JSON found
        return "{}";
    }

    /// <summary>
    /// Find matching closing bracket accounting for nesting.
    /// </summary>
    private static int FindMatchingBracket(string content, int start, char closeChar)
    {
        var openChar = closeChar == '}' ? '{' : '[';
        var depth = 0;
        var inString = false;
        var escape = false;

        for (var i = start; i < content.Length; i++)
        {
            var c = content[i];

            if (escape)
            {
                escape = false;
                continue;
            }

            if (c == '\\')
            {
                escape = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString) continue;

            if (c == openChar) depth++;
            else if (c == closeChar)
            {
                depth--;
                if (depth == 0) return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Parse JSON from LLM response into typed object.
    /// </summary>
    /// <typeparam name="T">Target type</typeparam>
    /// <param name="content">Raw LLM response content</param>
    /// <param name="options">JSON serializer options (optional)</param>
    /// <returns>Deserialized object or null if parsing fails</returns>
    public static T? ParseJson<T>(string content, JsonSerializerOptions? options = null) where T : class
    {
        try
        {
            var json = ExtractJson(content);
            return JsonSerializer.Deserialize<T>(json, options ?? DefaultOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Try to parse JSON from LLM response.
    /// </summary>
    /// <typeparam name="T">Target type</typeparam>
    /// <param name="content">Raw LLM response content</param>
    /// <param name="result">Parsed result</param>
    /// <param name="options">JSON serializer options (optional)</param>
    /// <returns>True if parsing succeeded</returns>
    public static bool TryParseJson<T>(string content, out T? result, JsonSerializerOptions? options = null) where T : class
    {
        result = ParseJson<T>(content, options);
        return result != null;
    }

    /// <summary>
    /// Parse JSON to JsonDocument for dynamic access.
    /// </summary>
    /// <param name="content">Raw LLM response content</param>
    /// <returns>JsonDocument or null if parsing fails</returns>
    public static JsonDocument? ParseToDocument(string content)
    {
        try
        {
            var json = ExtractJson(content);
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };
}

