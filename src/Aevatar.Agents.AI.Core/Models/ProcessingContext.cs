using System.Collections.Generic;
using Aevatar.Agents.AI.Core.Abstractions;
using Aevatar.Agents.AI.Abstractions;

namespace Aevatar.Agents.AI.Core.Models;

/// <summary>
/// Context for processing a chat request through a strategy.
/// </summary>
public class ProcessingContext
{
    /// <summary>
    /// Gets or sets the conversation manager.
    /// </summary>
    public IConversationManager Conversation { get; set; } = null!;

    /// <summary>
    /// Gets or sets the tool manager for function calling.
    /// </summary>
    public IAevatarToolManager? Tools { get; set; }

    /// <summary>
    /// Gets or sets the LLM provider.
    /// </summary>
    public IAevatarLLMProvider LLMProvider { get; set; } = null!;

    /// <summary>
    /// Gets or sets the system prompt.
    /// </summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the chat request being processed.
    /// </summary>
    public ChatRequest Request { get; set; } = null!;

    /// <summary>
    /// Gets or sets the AI configuration.
    /// </summary>
    public AIConfiguration Configuration { get; set; } = new();

    /// <summary>
    /// Gets or sets the memory manager for context retrieval.
    /// </summary>
    public IAevatarMemory? Memory { get; set; }

    /// <summary>
    /// Gets or sets shared state between processing steps.
    /// </summary>
    public Dictionary<string, object> SharedState { get; set; } = new();

    /// <summary>
    /// Gets or sets the maximum number of iterations for iterative strategies.
    /// </summary>
    public int MaxIterations { get; set; } = 10;

    /// <summary>
    /// Gets or sets whether to include detailed reasoning in the response.
    /// </summary>
    public bool IncludeReasoning { get; set; } = false;

    /// <summary>
    /// Gets or sets the cancellation token for the operation.
    /// </summary>
    public System.Threading.CancellationToken CancellationToken { get; set; }
}

/// <summary>
/// AI configuration settings.
/// </summary>
public class AIConfiguration
{
    /// <summary>
    /// Gets or sets the model to use.
    /// </summary>
    public string Model { get; set; } = "gpt-4";

    /// <summary>
    /// Gets or sets the temperature for response generation.
    /// </summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>
    /// Gets or sets the maximum tokens for response.
    /// </summary>
    public int MaxTokens { get; set; } = 2000;

    /// <summary>
    /// Gets or sets the top-p value for nucleus sampling.
    /// </summary>
    public double TopP { get; set; } = 1.0;

    /// <summary>
    /// Gets or sets the frequency penalty.
    /// </summary>
    public double FrequencyPenalty { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets the presence penalty.
    /// </summary>
    public double PresencePenalty { get; set; } = 0.0;

    /// <summary>
    /// Gets or sets custom model parameters.
    /// </summary>
    public Dictionary<string, object> CustomParameters { get; set; } = new();
}
