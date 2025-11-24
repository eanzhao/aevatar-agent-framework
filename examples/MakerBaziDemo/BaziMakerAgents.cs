using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.AI;
using Aevatar.Agents.Maker;
using Microsoft.Extensions.Logging;

namespace MakerBaziDemo;

public class BaziMakerTaskAgent : MakerTaskAgent
{
    private bool _plannerInitialized;
    private readonly SemaphoreSlim _plannerInitLock = new(1, 1);

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        CustomConfig.SystemPromptTemplate =
            """
            You coordinate MAKER style Bazi analysis for a single goal.
            - Always drive decomposition first, then confirm atomic solves.
            - Keep every child output structured and easy to merge.
            - Require clear justifications grounded in traditional Chinese metaphysics.
            - Escalate when proposals conflict or lack evidence.
            """;
        CustomConfig.ConsensusThresholdK = Math.Max(CustomConfig.ConsensusThresholdK, 2);
        CustomConfig.InitialFanOut = Math.Max(CustomConfig.InitialFanOut, 4);
        CustomConfig.MaxDepth = Math.Max(CustomConfig.MaxDepth, 5);
        CustomConfig.MaxCandidateWait = Math.Max(CustomConfig.MaxCandidateWait, 18);
        CustomConfig.ProviderName = string.IsNullOrWhiteSpace(CustomConfig.ProviderName)
            ? "deepseek"
            : CustomConfig.ProviderName;
        CustomConfig.WorkerResponseTokenLimit = Math.Min(CustomConfig.WorkerResponseTokenLimit, 120);
        CustomConfig.EnforceMicroSteps = true;
        CustomConfig.EnableSelfWorker = true;
        CustomConfig.WorkerStopSequences.Clear();
        CustomConfig.WorkerStopSequences.Add("<END>");
    }

    protected override async Task<IReadOnlyList<string>> BuildMicroObjectivesAsync(AssignTaskEvent evt, CancellationToken ct)
    {
        await EnsurePlannerInitializedAsync(ct);

        var prompt = BuildMicroPlannerPrompt(evt);
        var request = new ChatRequest
        {
            Message = prompt,
            RequestId = $"planner-{Guid.NewGuid():N}",
            Temperature = 0.1,
            MaxTokens = 800
        };
        request.StopSequences.Add("<DONE>");

        var builder = new StringBuilder();
        await foreach (var chunk in ChatStreamAsync(request, ct))
        {
            if (string.IsNullOrWhiteSpace(chunk))
            {
                continue;
            }

            builder.Append(chunk);
            Logger.LogInformation("🧭 MicroPlanner chunk: {Chunk}", chunk.Trim());
        }

        var raw = builder.ToString();
        Logger.LogInformation("🧭 MicroPlanner raw output:\n{Output}", raw);

        var objectives = ParseMicroPlannerOutput(raw);
        if (objectives.Count == 0)
        {
            Logger.LogWarning("Micro planner produced no objectives, using fallback stages.");
            objectives =
            [
                "核对排盘信息并列出缺失项。<END>",
                "判断格局与日主强弱，并指出喜忌。<END>",
                "基于前两步给出用神与忌神。<END>",
                "输出事业与健康各一条洞察。<END>",
                "总结两条建议与一个风险提醒。<END>"
            ];
        }

        return objectives;
    }

    private async Task EnsurePlannerInitializedAsync(CancellationToken ct)
    {
        if (_plannerInitialized)
        {
            return;
        }

        await _plannerInitLock.WaitAsync(ct);
        try
        {
            if (_plannerInitialized)
            {
                return;
            }

            await InitializeAsync(
                CustomConfig.ProviderName,
                config =>
                {
                    config.Model = "deepseek-chat";
                    config.MaxOutputTokens = 800;
                    config.Temperature = 0.2f;
                },
                ct);

            _plannerInitialized = true;
        }
        finally
        {
            _plannerInitLock.Release();
        }
    }

    private string BuildMicroPlannerPrompt(AssignTaskEvent evt)
    {
        var builder = new StringBuilder();
        builder.AppendLine("你是负责拆解命理任务的项目经理，需要把要求分成 4-6 个微阶段，每个阶段只关注一个问题。");
        builder.AppendLine("输出 JSON 数组，每个元素包含 {\"instruction\":\"...\",\"stop\":\"<END>\"}，instruction 60 字以内。");
        builder.AppendLine("最后以 <DONE> 结尾。");
        builder.AppendLine();
        builder.AppendLine("[任务描述]");
        builder.AppendLine(evt.GoalDescription);

        if (evt.ContextVariables.Count > 0)
        {
            builder.AppendLine("[上下文]");
            foreach (var pair in evt.ContextVariables)
            {
                builder.AppendLine($"- {pair.Key}: {pair.Value}");
            }
        }

        builder.AppendLine("[格式示例]");
        builder.AppendLine("""
[
  {"instruction":"核对出生干支是否一致，列出缺失排盘信息","stop":"<END>"},
  {"instruction":"判断日主强弱与喜忌，两句话说明","stop":"<END>"}
]
<DONE>
""");

        return builder.ToString();
    }

    private static List<string> ParseMicroPlannerOutput(string raw)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return list;
        }

        var payload = raw.Trim();
        var start = payload.IndexOf('[');
        var end = payload.LastIndexOf(']');
        if (start >= 0 && end > start)
        {
            payload = payload.Substring(start, end - start + 1);
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    var instruction = element.TryGetProperty("instruction", out var inst)
                        ? inst.GetString()
                        : element.TryGetProperty("description", out var desc)
                            ? desc.GetString()
                            : null;
                    var stop = element.TryGetProperty("stop", out var stopProp)
                        ? stopProp.GetString()
                        : "<END>";
                    if (!string.IsNullOrWhiteSpace(instruction))
                    {
                        list.Add($"{instruction.Trim()} {stop}".Trim());
                    }
                }
            }
        }
        catch (JsonException)
        {
            // ignore, fallback below
        }

        if (list.Count == 0)
        {
            foreach (var line in raw.Split('\n'))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) ||
                    trimmed.Equals("<DONE>", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                list.Add(trimmed.EndsWith("<END>", StringComparison.OrdinalIgnoreCase)
                    ? trimmed
                    : $"{trimmed} <END>");
            }
        }

        return list;
    }
}

public class BaziMakerWorkerAgent : MakerWorkerAgent
{
    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        SystemPrompt =
            """
            You are a micro-agent for Bazi reading.
            - Follow instructions precisely.
            - Prefer JSON arrays for decomposition and bullet lists for conclusions.
            - Cite Heavenly Stems, Earthly Branches, and Five Elements explicitly.
            - When solving atomically, provide a short reasoning section followed by a crisp answer.
            """;

        CustomConfig.DefaultModel = string.IsNullOrWhiteSpace(CustomConfig.DefaultModel)
            ? "deepseek-chat"
            : CustomConfig.DefaultModel;
        CustomConfig.Temperature = CustomConfig.Temperature <= 0 ? 0.2f : CustomConfig.Temperature;
    }
}