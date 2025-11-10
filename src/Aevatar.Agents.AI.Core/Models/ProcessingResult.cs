using System;
using System.Collections.Generic;
using Aevatar.Agents.AI.Core.Messages;

namespace Aevatar.Agents.AI.Core.Models;

/// <summary>
/// Result from a processing strategy execution.
/// </summary>
public class ProcessingResult
{
    /// <summary>
    /// Gets or sets the final response content.
    /// </summary>
    public string FinalResponse { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the processing steps taken.
    /// </summary>
    public List<ProcessingStep> Steps { get; set; } = new();

    /// <summary>
    /// Gets or sets whether the processing was successful.
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Gets or sets any error that occurred.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the token usage for this processing.
    /// </summary>
    public TokenUsage? TokenUsage { get; set; }

    /// <summary>
    /// Gets or sets the strategy that produced this result.
    /// </summary>
    public string StrategyName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the time taken to process in milliseconds.
    /// </summary>
    public long ProcessingTimeMs { get; set; }

    /// <summary>
    /// Gets or sets intermediate results from the processing.
    /// </summary>
    public List<IntermediateResult> IntermediateResults { get; set; } = new();

    /// <summary>
    /// Gets or sets tools that were invoked during processing.
    /// </summary>
    public List<ToolInvocation> ToolInvocations { get; set; } = new();

    /// <summary>
    /// Gets or sets the confidence score of the result (0-1).
    /// </summary>
    public double? ConfidenceScore { get; set; }

    /// <summary>
    /// Gets or sets metadata about the processing.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// Gets or sets whether further processing is needed.
    /// </summary>
    public bool RequiresContinuation { get; set; } = false;

    /// <summary>
    /// Gets or sets the continuation context if further processing is needed.
    /// </summary>
    public ContinuationContext? Continuation { get; set; }
}

/// <summary>
/// Represents an intermediate result during processing.
/// </summary>
public class IntermediateResult
{
    /// <summary>
    /// Gets or sets the type of intermediate result.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the content of the result.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when this result was generated.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets additional data.
    /// </summary>
    public Dictionary<string, object> Data { get; set; } = new();
}

/// <summary>
/// Represents a tool invocation during processing.
/// </summary>
public class ToolInvocation
{
    /// <summary>
    /// Gets or sets the tool name.
    /// </summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the arguments passed to the tool.
    /// </summary>
    public string Arguments { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the result from the tool.
    /// </summary>
    public string Result { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the tool call was successful.
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Gets or sets the execution time in milliseconds.
    /// </summary>
    public long ExecutionTimeMs { get; set; }

    /// <summary>
    /// Gets or sets when the tool was invoked.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Context for continuing a multi-step processing.
/// </summary>
public class ContinuationContext
{
    /// <summary>
    /// Gets or sets the continuation token.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the current step number.
    /// </summary>
    public int CurrentStep { get; set; }

    /// <summary>
    /// Gets or sets the total expected steps.
    /// </summary>
    public int? TotalSteps { get; set; }

    /// <summary>
    /// Gets or sets the state to resume from.
    /// </summary>
    public Dictionary<string, object> State { get; set; } = new();

    /// <summary>
    /// Gets or sets the next action to take.
    /// </summary>
    public string? NextAction { get; set; }
}
