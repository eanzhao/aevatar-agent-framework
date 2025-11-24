using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Maker;

/// <summary>
/// MAKER worker agent responsible for raw LLM execution.
/// </summary>
public class MakerWorkerAgent : AIGAgentBase<WorkerAgentState, WorkerAgentConfig>
{
    private const int WorkerPreviewLength = 160;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        if (string.IsNullOrWhiteSpace(CustomState.WorkerId))
        {
            CustomState.WorkerId = Id.ToString();
        }

        if (string.IsNullOrWhiteSpace(CustomConfig.ProviderName))
        {
            CustomConfig.ProviderName = "deepseek";
        }

        if (string.IsNullOrWhiteSpace(CustomConfig.DefaultModel))
        {
            CustomConfig.DefaultModel = "deepseek-chat";
        }

        if (CustomConfig.MaxOutputTokens <= 0)
        {
            CustomConfig.MaxOutputTokens = 2048;
        }

        if (CustomConfig.Temperature <= 0)
        {
            CustomConfig.Temperature = 0.2f;
        }

        SystemPrompt = """
You are a MAKER worker cell specializing in structured reasoning.
Follow instructions exactly, keep answers deterministic, and prefer JSON for plans.
""";
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_isInitialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(ct);
        try
        {
            if (_isInitialized)
            {
                return;
            }

            await InitializeAsync(
                CustomConfig.ProviderName,
                configAI: cfg =>
                {
                    cfg.Model = CustomConfig.DefaultModel;
                    cfg.MaxOutputTokens = CustomConfig.MaxOutputTokens;
                    cfg.Temperature = CustomConfig.Temperature;
                },
                cancellationToken: ct);
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleGenerateProposalAsync(GenerateProposalEvent evt)
    {
        await EnsureInitializedAsync(CancellationToken.None);

        CustomState.TotalRequests++;
        CustomState.LastRequestAt = Timestamp.FromDateTime(DateTime.UtcNow);

        var (prompt, role) = BuildPrompt(evt);
        CustomState.ActiveRole = role;

        Logger.LogInformation("Worker {WorkerId} generating {Role} proposal for request {RequestId}",
            CustomState.WorkerId, role, evt.RequestId);

        var chatRequest = BuildChatRequest(evt, prompt);
        var responseContent = await GenerateResponseWithStreamAsync(chatRequest, evt);

        Logger.LogInformation(
            "Worker {WorkerId} finished {Role} proposal for request {RequestId}. Preview={Preview}",
            CustomState.WorkerId,
            role,
            evt.RequestId,
            BuildPreview(responseContent));

        await PublishAsync(new ProposalReceivedEvent
        {
            RequestId = evt.RequestId,
            Content = responseContent,
            ReasoningTrace = $"role={role};worker={CustomState.WorkerId}"
        }, EventDirection.Up);
    }

    private ChatRequest BuildChatRequest(GenerateProposalEvent evt, string prompt)
    {
        var request = new ChatRequest
        {
            RequestId = $"{evt.RequestId}:{Guid.NewGuid():N}",
            Message = prompt
        };

        if (evt.MaxOutputTokens > 0)
        {
            request.MaxTokens = evt.MaxOutputTokens;
        }

        if (evt.StopSequences.Count > 0)
        {
            foreach (var seq in evt.StopSequences)
            {
                request.StopSequences.Add(seq);
            }
        }

        if (!string.IsNullOrWhiteSpace(evt.StageHint))
        {
            request.StageHint = evt.StageHint;
        }

        return request;
    }

    private async Task<string> GenerateResponseWithStreamAsync(ChatRequest request, GenerateProposalEvent evt)
    {
        var builder = new StringBuilder();
        await foreach (var chunk in ChatStreamAsync(request))
        {
            if (string.IsNullOrWhiteSpace(chunk))
            {
                continue;
            }

            var safeChunk = chunk.ReplaceLineEndings(" ").Replace("|", "¦").Trim();
            builder.Append(safeChunk);
            Logger.LogInformation("WORKER_STREAM|{WorkerId}|{RequestId}|{Chunk}",
                CustomState.WorkerId,
                evt.RequestId,
                safeChunk);
        }

        var content = builder.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(content))
        {
            Logger.LogInformation("WORKER_DONE|{WorkerId}|{RequestId}|length={Length}",
                CustomState.WorkerId,
                evt.RequestId,
                content.Length);
            return content;
        }

        var fallback = await ChatAsync(request);
        Logger.LogInformation("WORKER_DONE|{WorkerId}|{RequestId}|fallback", CustomState.WorkerId, evt.RequestId);
        return fallback.Content;
    }

    private static (string prompt, string role) BuildPrompt(GenerateProposalEvent evt)
    {
        var builder = new StringBuilder();
        string role;

        if (evt.Type == GenerateProposalEvent.Types.GenerationType.Decomposition)
        {
            role = "decomposer";
            builder.AppendLine("You decompose the goal into an ordered JSON array of atomic steps.");
            builder.AppendLine("Each entry must be an object: { \"step_id\": \"S1\", \"description\": \"...\" }.");
            builder.AppendLine("Do not include commentary outside the JSON block.");
            builder.AppendLine("Goal:");
        }
        else
        {
            role = "solver";
            builder.AppendLine("You execute an atomic task and respond with the final answer.");
            builder.AppendLine("Provide clear reasoning bullets before the final answer if necessary.");
            builder.AppendLine("Task:");
        }

        builder.AppendLine(evt.TaskDescription);
        return (builder.ToString(), role);
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"MAKER Worker ({CustomState.TotalRequests} requests handled)");
    }

    private static string BuildPreview(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "[empty]";
        }

        var normalized = content.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= WorkerPreviewLength
            ? normalized
            : normalized[..WorkerPreviewLength] + "...";
    }
}



