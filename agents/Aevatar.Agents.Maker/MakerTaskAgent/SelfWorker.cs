using System.Text;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;

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

        var request = new ChatRequest
        {
            Message = prompt,
            RequestId = $"{evt.RequestId}:self",
            Temperature = 0.2,
            MaxTokens = evt.MaxOutputTokens > 0 ? Math.Max(evt.MaxOutputTokens, 1000) : 1000
        };

        foreach (var seq in evt.StopSequences)
        {
            request.StopSequences.Add(seq);
        }

        if (!string.IsNullOrWhiteSpace(evt.StageHint))
        {
            request.StageHint = evt.StageHint;
        }

        var builder = new StringBuilder();
        await foreach (var chunk in ChatStreamAsync(request, ct))
        {
            if (string.IsNullOrWhiteSpace(chunk))
            {
                continue;
            }

            builder.Append(chunk);
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
}

