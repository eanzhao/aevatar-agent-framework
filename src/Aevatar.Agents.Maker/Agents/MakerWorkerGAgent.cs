using System.Diagnostics;
using System.Text;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Maker.Messages;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Aevatar.Agents.AI;

namespace Aevatar.Agents.Maker.Agents;

// ============================================================
//  MAKER Worker Agent
//  Pure event-driven with Streaming LLM support
//  
//  Optimizations:
//  - Streaming LLM responses for lower latency
//  - Cancellation support for early termination
//  - TTFT (Time To First Token) monitoring
//  - Progressive format validation
// ============================================================

/// <summary>
/// MAKER Worker Agent - executes LLM proposal generations.
/// All communication happens through events (no direct method calls).
/// 
/// Event Flow:
/// 1. Receives InitializeWorkerRequest -> Initializes LLM -> Publishes WorkerInitialized
/// 2. Receives GenerateProposalRequest -> Calls LLM (streaming) -> Publishes ProposalResult
/// 3. Receives CancelCurrentRequest -> Cancels ongoing LLM call (early termination)
/// </summary>
public class MakerWorkerGAgent : AIGAgentBase<MakerWorkerState, MakerWorkerConfig>
{
    // Cancellation support for early termination
    private CancellationTokenSource? _currentRequestCts;
    private readonly object _ctsLock = new();

    public MakerWorkerGAgent() { }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"MAKER Worker [{CustomState.WorkerId}] - Status: {CustomState.Status}");
    }

    #region Event Handlers

    /// <summary>
    /// Handle initialization request from coordinator.
    /// </summary>
    [EventHandler]
    public async Task HandleInitializeWorkerRequest(InitializeWorkerRequest request)
    {
        Logger.LogDebug("Worker {Id} received initialization request from {CoordinatorId}",
            Id, request.CoordinatorId);

        try
        {
            // Initialize AI capabilities
            await InitializeAsync(request.ProviderName, config =>
            {
                config.Temperature = request.Temperature;
                config.MaxOutputTokens = 2048;
            });

            // Setup state
            CustomState.WorkerId = Id.ToString("N")[..8];
            CustomState.CoordinatorId = request.CoordinatorId;
            CustomState.Status = 0; // Idle
            CustomState.TotalProposals = 0;
            CustomState.SuccessfulProposals = 0;
            CustomState.FailedProposals = 0;

            // Setup config
            CustomConfig.LlmProviderName = request.ProviderName;
            CustomConfig.DefaultTemperature = request.Temperature;
            CustomConfig.DefaultMaxTokens = 2048;
            CustomConfig.WorkerIndex = request.WorkerIndex;

            // Notify coordinator that worker is ready (UP to parent)
            await PublishAsync(new WorkerInitialized
            {
                WorkerId = CustomState.WorkerId,
                CoordinatorId = request.CoordinatorId,
                WorkerIndex = request.WorkerIndex
            }, EventDirection.Up);

            Logger.LogInformation("Worker {WorkerId} initialized for coordinator {CoordinatorId}",
                CustomState.WorkerId, request.CoordinatorId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Worker {Id} failed to initialize", Id);
            throw;
        }
    }

    /// <summary>
    /// Handle cancellation request from coordinator (early termination).
    /// </summary>
    [EventHandler]
    public Task HandleCancelCurrentRequest(CancelCurrentRequest request)
    {
        lock (_ctsLock)
        {
            if (_currentRequestCts != null && !_currentRequestCts.IsCancellationRequested)
            {
                Logger.LogWarning("[CANCEL] Worker {WorkerId} received cancel request (reason: {Reason}), cancelling LLM call",
                    CustomState.WorkerId, request.Reason);
                _currentRequestCts.Cancel();
            }
            else
            {
                Logger.LogWarning("[CANCEL] Worker {WorkerId} received cancel but no active request to cancel (already finished?)",
                    CustomState.WorkerId);
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handle proposal generation request from coordinator.
    /// Uses streaming for lower latency and supports early termination.
    /// </summary>
    [EventHandler]
    public async Task HandleGenerateProposalRequest(GenerateProposalRequest request)
    {
        CustomState.CurrentTaskId = request.TaskId;
        CustomState.CurrentRequestId = request.RequestId;
        CustomState.Status = 1; // Working
        CustomState.LastActivity = Timestamp.FromDateTime(DateTime.UtcNow);

        var startTime = Stopwatch.GetTimestamp();
        
        Logger.LogDebug("Worker {WorkerId} processing request {RequestId} for task {TaskId}",
            CustomState.WorkerId, request.RequestId, request.TaskId);

        // Create cancellation token for this request
        CancellationTokenSource cts;
        lock (_ctsLock)
        {
            _currentRequestCts?.Dispose();
            _currentRequestCts = new CancellationTokenSource();
            cts = _currentRequestCts;
        }

        string? content = null;
        string? error = null;
        var success = false;
        int promptTokens = 0, completionTokens = 0, totalTokens = 0;
        long? ttftMs = null;

        // Generate proposal ID early for streaming events
        var proposalId = request.IsDecomposition
            ? $"D{CustomState.TotalProposals + 1}"
            : $"S{CustomState.TotalProposals + 1}";

        try
        {
            var llmRequest = new AevatarLLMRequest
            {
                SystemPrompt = request.SystemPrompt,
                Settings = new AevatarLLMSettings
                {
                    Temperature = request.Temperature,
                    MaxTokens = request.MaxTokens
                },
                Messages = [new AevatarChatMessage { Role = AevatarChatRole.User, Content = request.UserPrompt }]
            };

            // Try streaming first, fallback to non-streaming
            var modelInfo = await LLMProvider.GetModelInfoAsync(cts.Token);
            
            if (modelInfo.SupportsStreaming)
            {
                (content, promptTokens, completionTokens, ttftMs) = 
                    await GenerateWithStreamingAsync(llmRequest, request.IsDecomposition, startTime, proposalId, cts.Token);
            }
            else
            {
                // Fallback: non-streaming with cancellation support
                var response = await LLMProvider.GenerateAsync(llmRequest, cts.Token);
                content = response.Content;
                
                if (response.Usage != null)
                {
                    promptTokens = response.Usage.PromptTokens;
                    completionTokens = response.Usage.CompletionTokens;
                }
            }

            totalTokens = promptTokens + completionTokens;
            success = !string.IsNullOrWhiteSpace(content);

            if (success)
            {
                CustomState.SuccessfulProposals++;
            }
            else
            {
                CustomState.FailedProposals++;
                error = "Empty response from LLM";
            }
        }
        catch (OperationCanceledException)
        {
            Logger.LogInformation("Worker {WorkerId} request {RequestId} was cancelled (early termination)",
                CustomState.WorkerId, request.RequestId);
            error = "Cancelled (early termination - consensus reached)";
            CustomState.FailedProposals++;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Worker {WorkerId} failed to generate proposal for {RequestId}",
                CustomState.WorkerId, request.RequestId);
            error = ex.Message;
            CustomState.FailedProposals++;
        }
        finally
        {
            lock (_ctsLock)
            {
                if (_currentRequestCts == cts)
                {
                    _currentRequestCts = null;
                }
            }
            cts.Dispose();
        }

        var latencyMs = (long)((Stopwatch.GetTimestamp() - startTime) * 1000.0 / Stopwatch.Frequency);
        
        CustomState.TotalProposals++;
        CustomState.Status = 0; // Back to idle

        // Send result back to coordinator (UP direction to parent stream)
        await PublishAsync(new ProposalResult
        {
            RequestId = request.RequestId,
            TaskId = request.TaskId,
            WorkerId = CustomState.WorkerId,
            ProposalId = proposalId,
            Content = content ?? string.Empty,
            Success = success,
            Error = error ?? string.Empty,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = totalTokens,
            LatencyMs = latencyMs,
            ProviderName = CustomConfig.LlmProviderName,
            TtftMs = ttftMs ?? 0  // Time To First Token
        }, EventDirection.Up);
        
        Logger.LogDebug(
            "Worker {WorkerId} completed request {RequestId} in {LatencyMs}ms (TTFT: {TTFT}ms, tokens: {Prompt}+{Completion}={Total})",
            CustomState.WorkerId, request.RequestId, latencyMs, ttftMs ?? -1, promptTokens, completionTokens, totalTokens);
    }

    #endregion

    #region Streaming Support

    /// <summary>
    /// Generate LLM response using streaming for lower latency.
    /// Publishes real-time StreamingToken events for live UI updates.
    /// </summary>
    private async Task<(string Content, int PromptTokens, int CompletionTokens, long? TtftMs)> GenerateWithStreamingAsync(
        AevatarLLMRequest request,
        bool isDecomposition,
        long startTimestamp,
        string proposalId,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        var firstTokenReceived = false;
        long? ttftMs = null;
        int promptTokens = 0, completionTokens = 0;
        var tokenIndex = 0;

        Logger.LogWarning("[STREAMING] Worker {WorkerId} starting streaming for task {TaskId}, proposalId={ProposalId}",
            CustomState.WorkerId, CustomState.CurrentTaskId, proposalId);

        await foreach (var token in LLMProvider.GenerateStreamAsync(request, ct))
        {
            // Track Time To First Token
            var isFirst = !firstTokenReceived;
            if (isFirst)
            {
                firstTokenReceived = true;
                ttftMs = (long)((Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency);
                
                Logger.LogWarning("[STREAMING] Worker {WorkerId} first token received, TTFT: {TtftMs}ms, token: '{Token}'",
                    CustomState.WorkerId, ttftMs, 
                    token.Content?.Length > 50 ? token.Content[..50] + "..." : token.Content);
            }

            // Accumulate content
            if (!string.IsNullOrEmpty(token.Content))
            {
                sb.Append(token.Content);
                
                // Publish streaming token event for real-time UI updates
                await PublishAsync(new StreamingToken
                {
                    RequestId = CustomState.CurrentRequestId,
                    TaskId = CustomState.CurrentTaskId,
                    WorkerId = CustomState.WorkerId,
                    ProposalId = proposalId,
                    Token = token.Content,
                    AccumulatedContent = sb.ToString(),
                    TokenIndex = tokenIndex++,
                    IsFirstToken = isFirst,
                    IsLastToken = false,
                    Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                    ProviderName = CustomConfig.LlmProviderName
                }, EventDirection.Up, ct);
            }

            // Early format validation for decomposition (JSON expected)
            if (isDecomposition && sb.Length > 200)
            {
                var partial = sb.ToString();
                if (IsObviouslyInvalidJson(partial))
                {
                    Logger.LogWarning("Worker {WorkerId} detected invalid JSON format early, continuing anyway",
                        CustomState.WorkerId);
                    // Note: We don't cancel here - let the LLM finish and report the error
                    // Cancellation would waste the already-consumed tokens
                }
            }
        }

        // Send final token marker
        await PublishAsync(new StreamingToken
        {
            RequestId = CustomState.CurrentRequestId,
            TaskId = CustomState.CurrentTaskId,
            WorkerId = CustomState.WorkerId,
            ProposalId = proposalId,
            Token = string.Empty,
            AccumulatedContent = sb.ToString(),
            TokenIndex = tokenIndex,
            IsFirstToken = false,
            IsLastToken = true,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            ProviderName = CustomConfig.LlmProviderName
        }, EventDirection.Up, ct);

        // Estimate completion tokens if not provided
        if (completionTokens == 0 && sb.Length > 0)
        {
            // Rough estimate: ~4 chars per token for English
            completionTokens = sb.Length / 4;
        }

        return (sb.ToString(), promptTokens, completionTokens, ttftMs);
    }

    /// <summary>
    /// Quick check if partial content is obviously not valid JSON.
    /// Used for early detection of format errors.
    /// </summary>
    private static bool IsObviouslyInvalidJson(string partial)
    {
        var trimmed = partial.TrimStart();
        
        // JSON should start with { or [
        if (trimmed.Length > 0 && trimmed[0] != '{' && trimmed[0] != '[')
        {
            // Allow markdown code blocks
            if (trimmed.StartsWith("```"))
                return false;
            
            return true;
        }
        
        return false;
    }

    #endregion
}
