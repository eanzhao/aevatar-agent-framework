using System;
using System.Collections.Generic;
using Aevatar.Agents.AI.Core.Messages;

namespace Aevatar.Agents.AI.Core.Models;

/// <summary>
/// Represents a response from an AI agent.
/// </summary>
public class ChatResponse
{
    /// <summary>
    /// Gets or sets the request ID this response is for.
    /// </summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the main response content.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the processing steps if requested.
    /// </summary>
    public List<ProcessingStep> ProcessingSteps { get; set; } = new();

    /// <summary>
    /// Gets or sets the token usage information.
    /// </summary>
    public TokenUsage? Usage { get; set; }

    /// <summary>
    /// Gets or sets the processing strategy that was used.
    /// </summary>
    public string? StrategyUsed { get; set; }

    /// <summary>
    /// Gets or sets the processing duration in milliseconds.
    /// </summary>
    public long ProcessingTimeMs { get; set; }

    /// <summary>
    /// Gets or sets whether the response is complete.
    /// </summary>
    public bool IsComplete { get; set; } = true;

    /// <summary>
    /// Gets or sets a continuation token for multi-turn responses.
    /// </summary>
    public string? ContinuationToken { get; set; }

    /// <summary>
    /// Gets or sets any error that occurred during processing.
    /// </summary>
    public ProcessingError? Error { get; set; }

    /// <summary>
    /// Gets or sets response metadata.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// Gets or sets the timestamp when the response was generated.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Represents an error that occurred during processing.
/// </summary>
public class ProcessingError
{
    /// <summary>
    /// Gets or sets the error code.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the error message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets additional error details.
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// Gets or sets whether the error is recoverable.
    /// </summary>
    public bool IsRecoverable { get; set; }

    /// <summary>
    /// Gets or sets suggested actions to resolve the error.
    /// </summary>
    public List<string> SuggestedActions { get; set; } = new();
}
