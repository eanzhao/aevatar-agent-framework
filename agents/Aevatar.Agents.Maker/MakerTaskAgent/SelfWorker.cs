using System.Collections.Generic;
using System.Linq;
using System.Text;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Messages;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Maker;

public partial class MakerTaskAgent
{
    private string BuildSelfWorkerPrompt(GenerateProposalEvent evt)
    {
        var builder = new StringBuilder();
        builder.AppendLine(CustomState.ActiveGenerationType ==
                           TaskAgentState.Types.GenerationRequestType.Decomposition
            ? "You are the fallback MAKER decomposition unit. Produce a candidate plan quickly when external votes are missing."
            : "You are the fallback MAKER atomic solver. Produce a minimal yet reliable answer when external votes are missing.");
        builder.AppendLine("Follow these rules:");
        builder.AppendLine("1. Keep responses concise - no extra commentary.");
        builder.AppendLine("2. For decomposition tasks, output a JSON array; for atomic tasks, use no more than three lines.");
        builder.AppendLine("3. Append the <END> marker and stop immediately.");
        builder.AppendLine();
        builder.AppendLine("[Current Task]");
        builder.AppendLine(evt.TaskDescription);
        return builder.ToString();
    }

    private async Task<string> RunSelfWorkerAsync(
        string prompt,
        GenerateProposalEvent evt,
        CancellationToken ct)
    {
        await EnsureSelfWorkerInitializedAsync(ct);

        var llmRequest = BuildSelfWorkerRequest(prompt, evt);
        var builder = new StringBuilder();

        var enumerator = LLMProvider.GenerateStreamAsync(llmRequest, ct).GetAsyncEnumerator(ct);
        try
        {
            while (await enumerator.MoveNextAsync())
            {
                var chunk = enumerator.Current;
                if (!string.IsNullOrWhiteSpace(chunk.Content))
                {
                    var safeChunk = chunk.Content.ReplaceLineEndings(" ").Replace("|", "/");
                    builder.Append(chunk.Content);
                    Logger.LogInformation("WORKER_STREAM|{WorkerId}|{RequestId}|{Chunk}",
                        Id.ToString(),
                        evt.RequestId,
                        safeChunk);
                }

                if (chunk.IsComplete)
                {
                    break;
                }
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        return builder.ToString().Trim();
    }

    private async Task EnsureSelfWorkerInitializedAsync(CancellationToken ct)
    {
        if (_selfWorkerInitialized)
        {
            return;
        }

        await _selfWorkerInitLock.WaitAsync(ct);
        try
        {
            if (_selfWorkerInitialized)
            {
                return;
            }

            await InitializeAsync(CustomConfig.ProviderName, config =>
            {
                if (string.IsNullOrWhiteSpace(config.Model))
                {
                    config.Model = "deepseek-chat";
                }
                config.MaxOutputTokens = Math.Max(config.MaxOutputTokens, 800);
                config.Temperature = Math.Min(config.Temperature, 0.3f);
            }, ct);

            _selfWorkerInitialized = true;
        }
        finally
        {
            _selfWorkerInitLock.Release();
        }
    }

    private AevatarLLMRequest BuildSelfWorkerRequest(string prompt, GenerateProposalEvent evt)
    {
        var maxTokens = evt.MaxOutputTokens > 0 ? Math.Max(evt.MaxOutputTokens, 1000) : 1000;

        var settings = new AevatarLLMSettings
        {
            Temperature = 0.2f,
            MaxTokens = maxTokens,
            ModelId = Config.Model
        };

        var request = new AevatarLLMRequest
        {
            SystemPrompt = SystemPrompt,
            Settings = settings,
            Messages = new List<AevatarChatMessage>
            {
                new()
                {
                    Role = AevatarChatRole.User,
                    Content = prompt
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(evt.StageHint))
        {
            request.Context = new Dictionary<string, object>
            {
                ["stage_hint"] = evt.StageHint!
            };
        }

        if (evt.StopSequences.Count > 0)
        {
            request.Settings.StopSequences = evt.StopSequences.ToList();
        }

        return request;
    }
}

