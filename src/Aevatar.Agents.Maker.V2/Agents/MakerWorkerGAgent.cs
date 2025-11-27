using System.Diagnostics;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Maker.V2.Messages;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Aevatar.Agents.AI;

namespace Aevatar.Agents.Maker.V2.Agents;

// ============================================================
//  MAKER Worker Agent
//  Pure event-driven - ALL communication via Stream
// ============================================================

/// <summary>
/// MAKER Worker Agent - executes LLM proposal generations.
/// All communication happens through events (no direct method calls).
/// 
/// Event Flow:
/// 1. Receives InitializeWorkerRequest -> Initializes LLM -> Publishes WorkerInitialized
/// 2. Receives GenerateProposalRequest -> Calls LLM -> Publishes ProposalResult
/// </summary>
public class MakerWorkerGAgent : AIGAgentBase<MakerWorkerState, MakerWorkerConfig>
{
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
    /// Handle proposal generation request from coordinator.
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

        string? content = null;
        string? error = null;
        var success = false;
        int promptTokens = 0, completionTokens = 0, totalTokens = 0;

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

            var response = await LLMProvider.GenerateAsync(llmRequest);
            content = response.Content;
            success = !string.IsNullOrWhiteSpace(content);
            
            // Collect token usage
            if (response.Usage != null)
            {
                promptTokens = response.Usage.PromptTokens;
                completionTokens = response.Usage.CompletionTokens;
                totalTokens = response.Usage.TotalTokens;
            }

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
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Worker {WorkerId} failed to generate proposal for {RequestId}",
                CustomState.WorkerId, request.RequestId);
            error = ex.Message;
            CustomState.FailedProposals++;
        }

        var latencyMs = (long)((Stopwatch.GetTimestamp() - startTime) * 1000.0 / Stopwatch.Frequency);
        
        CustomState.TotalProposals++;
        CustomState.Status = 0; // Back to idle

        // Generate proposal ID
        var proposalId = request.IsDecomposition
            ? $"D{CustomState.TotalProposals}"
            : $"S{CustomState.TotalProposals}";

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
            LatencyMs = latencyMs
        }, EventDirection.Up);
        
        Logger.LogDebug(
            "Worker {WorkerId} completed request {RequestId} in {LatencyMs}ms (tokens: {Prompt}+{Completion}={Total})",
            CustomState.WorkerId, request.RequestId, latencyMs, promptTokens, completionTokens, totalTokens);
    }

    #endregion
}
