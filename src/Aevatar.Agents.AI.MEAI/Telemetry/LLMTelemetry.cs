using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Aevatar.Agents.AI.MEAI.Telemetry;

/// <summary>
/// OpenTelemetry instrumentation for LLM operations.
/// Provides traces and metrics for Aspire Dashboard monitoring.
/// </summary>
public static class LLMTelemetry
{
    // -------------------------------------------------------------------------
    // Configuration
    // -------------------------------------------------------------------------
    
    /// <summary>
    /// Maximum length for prompt/response content in traces.
    /// Set to 0 to disable content logging.
    /// </summary>
    public static int MaxContentLength { get; set; } = 2000;
    
    /// <summary>
    /// Enable/disable logging of prompt content (may contain sensitive data).
    /// </summary>
    public static bool LogPromptContent { get; set; } = true;
    
    /// <summary>
    /// Enable/disable logging of response content.
    /// </summary>
    public static bool LogResponseContent { get; set; } = true;
    
    // -------------------------------------------------------------------------
    // Activity Source (Traces)
    // -------------------------------------------------------------------------
    
    /// <summary>
    /// Activity source for LLM operations - shows up in Aspire Traces tab
    /// </summary>
    public static readonly ActivitySource Source = new("Aevatar.Agents.LLM", "1.0.0");
    
    // -------------------------------------------------------------------------
    // Metrics
    // -------------------------------------------------------------------------
    
    private static readonly Meter Meter = new("Aevatar.Agents.LLM", "1.0.0");
    
    /// <summary>
    /// Total LLM requests counter
    /// </summary>
    public static readonly Counter<long> RequestCount = 
        Meter.CreateCounter<long>("aevatar.llm.requests", "requests", "Total LLM API requests");
    
    /// <summary>
    /// Streaming token counter
    /// </summary>
    public static readonly Counter<long> StreamingTokens = 
        Meter.CreateCounter<long>("aevatar.llm.streaming.tokens", "tokens", "Total streaming tokens received");
    
    /// <summary>
    /// Time to first token histogram
    /// </summary>
    public static readonly Histogram<double> TimeToFirstToken = 
        Meter.CreateHistogram<double>("aevatar.llm.ttft", "ms", "Time to first token (TTFT)");
    
    /// <summary>
    /// Total response time histogram
    /// </summary>
    public static readonly Histogram<double> ResponseTime = 
        Meter.CreateHistogram<double>("aevatar.llm.response_time", "ms", "Total LLM response time");
    
    /// <summary>
    /// Input tokens counter
    /// </summary>
    public static readonly Counter<long> InputTokens = 
        Meter.CreateCounter<long>("aevatar.llm.tokens.input", "tokens", "Total input tokens");
    
    /// <summary>
    /// Output tokens counter
    /// </summary>
    public static readonly Counter<long> OutputTokens = 
        Meter.CreateCounter<long>("aevatar.llm.tokens.output", "tokens", "Total output tokens");
    
    // -------------------------------------------------------------------------
    // Span Helpers
    // -------------------------------------------------------------------------
    
    /// <summary>
    /// Start a trace span for LLM generation
    /// </summary>
    public static Activity? StartGeneration(string model, string operationType = "generate")
    {
        var activity = Source.StartActivity($"LLM {operationType}", ActivityKind.Client);
        activity?.SetTag("llm.model", model);
        activity?.SetTag("llm.operation", operationType);
        activity?.SetTag("llm.provider", "MEAI");
        return activity;
    }
    
    /// <summary>
    /// Start a trace span for streaming generation
    /// </summary>
    public static Activity? StartStreaming(string model)
    {
        var activity = Source.StartActivity("LLM streaming", ActivityKind.Client);
        activity?.SetTag("llm.model", model);
        activity?.SetTag("llm.operation", "streaming");
        activity?.SetTag("llm.provider", "MEAI");
        return activity;
    }
    
    /// <summary>
    /// Add streaming chunk event to current activity
    /// </summary>
    public static void AddStreamingChunk(Activity? activity, int chunkIndex, int chunkLength)
    {
        activity?.AddEvent(new ActivityEvent("streaming.chunk", tags: new ActivityTagsCollection
        {
            { "chunk.index", chunkIndex },
            { "chunk.length", chunkLength }
        }));
    }
    
    /// <summary>
    /// Record first token received
    /// </summary>
    public static void RecordFirstToken(Activity? activity, double ttftMs)
    {
        activity?.AddEvent(new ActivityEvent("streaming.first_token", tags: new ActivityTagsCollection
        {
            { "ttft_ms", ttftMs }
        }));
        TimeToFirstToken.Record(ttftMs);
    }
    
    /// <summary>
    /// Complete streaming span with final stats
    /// </summary>
    public static void CompleteStreaming(Activity? activity, int totalChunks, int totalTokens, double durationMs)
    {
        activity?.SetTag("llm.streaming.chunks", totalChunks);
        activity?.SetTag("llm.streaming.tokens", totalTokens);
        activity?.SetTag("llm.duration_ms", durationMs);
        
        StreamingTokens.Add(totalTokens);
        ResponseTime.Record(durationMs);
    }
    
    /// <summary>
    /// Record error in LLM operation
    /// </summary>
    public static void RecordError(Activity? activity, Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
        {
            { "exception.type", ex.GetType().Name },
            { "exception.message", ex.Message }
        }));
    }
    
    // -------------------------------------------------------------------------
    // Request/Response Content Logging
    // -------------------------------------------------------------------------
    
    /// <summary>
    /// Record the full conversation request for debugging.
    /// Includes system prompt, chat history, and final user prompt.
    /// </summary>
    public static void RecordRequest(
        Activity? activity, 
        string? systemPrompt, 
        string? userPrompt,
        IEnumerable<(string Role, string Content)>? messages = null)
    {
        if (activity == null || !LogPromptContent || MaxContentLength <= 0)
            return;
        
        // Record system prompt as separate event
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            activity.AddEvent(new ActivityEvent("llm.prompt.system", tags: new ActivityTagsCollection
            {
                { "content", Truncate(systemPrompt, MaxContentLength) }
            }));
        }
        
        // Record each message in conversation history
        if (messages != null)
        {
            var index = 0;
            foreach (var (role, content) in messages)
            {
                activity.AddEvent(new ActivityEvent($"llm.prompt.message[{index}]", tags: new ActivityTagsCollection
                {
                    { "role", role },
                    { "content", Truncate(content, MaxContentLength) }
                }));
                index++;
            }
        }
        
        // Record final user prompt (may be same as last message or additional)
        if (!string.IsNullOrEmpty(userPrompt))
        {
            activity.AddEvent(new ActivityEvent("llm.prompt.user", tags: new ActivityTagsCollection
            {
                { "content", Truncate(userPrompt, MaxContentLength) }
            }));
        }
    }
    
    /// <summary>
    /// Record request with simple string prompts (backward compatible).
    /// </summary>
    public static void RecordRequest(Activity? activity, string? systemPrompt, string? userPrompt)
    {
        RecordRequest(activity, systemPrompt, userPrompt, null);
    }
    
    /// <summary>
    /// Record the LLM response content for debugging.
    /// Content is truncated to MaxContentLength.
    /// </summary>
    public static void RecordResponse(Activity? activity, string? content)
    {
        if (activity == null || !LogResponseContent || MaxContentLength <= 0 || string.IsNullOrEmpty(content))
            return;
            
        activity.AddEvent(new ActivityEvent("llm.response", tags: new ActivityTagsCollection
        {
            { "llm.response.content", Truncate(content, MaxContentLength) },
            { "llm.response.length", content.Length }
        }));
    }
    
    /// <summary>
    /// Record streaming response completion with full content.
    /// </summary>
    public static void RecordStreamingResponse(Activity? activity, string? fullContent)
    {
        if (activity == null || !LogResponseContent || MaxContentLength <= 0 || string.IsNullOrEmpty(fullContent))
            return;
            
        activity.AddEvent(new ActivityEvent("llm.response.complete", tags: new ActivityTagsCollection
        {
            { "llm.response.content", Truncate(fullContent, MaxContentLength) },
            { "llm.response.length", fullContent.Length }
        }));
    }
    
    /// <summary>
    /// Truncate content to max length with ellipsis indicator.
    /// </summary>
    private static string Truncate(string content, int maxLength)
    {
        if (string.IsNullOrEmpty(content) || content.Length <= maxLength)
            return content;
            
        return content[..(maxLength - 20)] + $"... [truncated, total {content.Length} chars]";
    }
}

