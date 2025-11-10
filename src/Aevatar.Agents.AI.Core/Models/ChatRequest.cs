using System.Collections.Generic;

namespace Aevatar.Agents.AI.Core.Models;

/// <summary>
/// Represents a chat request to an AI agent.
/// </summary>
public class ChatRequest
{
    /// <summary>
    /// Gets or sets the unique identifier for this request.
    /// </summary>
    public string RequestId { get; set; } = System.Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets the message from the user.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets additional context for the request.
    /// </summary>
    public Dictionary<string, object> Context { get; set; } = new();

    /// <summary>
    /// Gets or sets the user identifier making the request.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Gets or sets the session identifier for conversation continuity.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Gets or sets processing hints for strategy selection.
    /// </summary>
    public ProcessingHints Hints { get; set; } = new();

    /// <summary>
    /// Gets or sets the maximum response length in tokens.
    /// </summary>
    public int? MaxResponseTokens { get; set; }

    /// <summary>
    /// Gets or sets whether to include processing steps in the response.
    /// </summary>
    public bool IncludeProcessingSteps { get; set; } = false;

    /// <summary>
    /// Gets or sets custom metadata for the request.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// Processing hints to guide strategy selection and execution.
/// </summary>
public class ProcessingHints
{
    /// <summary>
    /// Gets or sets the preferred processing strategy name.
    /// </summary>
    public string? PreferredStrategy { get; set; }

    /// <summary>
    /// Gets or sets whether to use reasoning steps.
    /// </summary>
    public bool? UseReasoning { get; set; }

    /// <summary>
    /// Gets or sets whether tool usage is expected.
    /// </summary>
    public bool? ExpectsToolUse { get; set; }

    /// <summary>
    /// Gets or sets the complexity level hint (0-1).
    /// </summary>
    public double? ComplexityHint { get; set; }

    /// <summary>
    /// Gets or sets the urgency level (affects processing depth).
    /// </summary>
    public UrgencyLevel Urgency { get; set; } = UrgencyLevel.Normal;

    /// <summary>
    /// Gets or sets custom hint parameters.
    /// </summary>
    public Dictionary<string, object> CustomHints { get; set; } = new();
}

/// <summary>
/// Urgency levels for processing.
/// </summary>
public enum UrgencyLevel
{
    /// <summary>Low urgency - can use deeper processing.</summary>
    Low,
    /// <summary>Normal urgency - balanced processing.</summary>
    Normal,
    /// <summary>High urgency - faster processing preferred.</summary>
    High,
    /// <summary>Critical - immediate response required.</summary>
    Critical
}
