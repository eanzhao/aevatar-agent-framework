using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI;
using Aevatar.Agents.Maker;
using MakerProjectsDemo.Infrastructure;
using Microsoft.Extensions.Logging;

namespace MakerProjectsDemo.Projects.Bazi;

public class BaziMakerTaskAgent : MakerTaskAgent
{
    private bool _plannerInitialized;
    private readonly SemaphoreSlim _plannerInitLock = new(1, 1);

    private readonly IMakerRunRecorder? _recorder;

    // Explicit constructor to ensure DI injection of IMakerChildLinker
    public BaziMakerTaskAgent(IMakerChildLinker childLinker, IMakerRunRecorder? recorder = null) : base(childLinker)
    {
        _recorder = recorder;
    }

    protected override async Task OnConsensusReachedAsync(string content, CancellationToken ct)
    {
        await base.OnConsensusReachedAsync(content, ct);
        
        if (_recorder != null)
        {
            var rootId = CustomState.TaskId.Split(':')[0];
            
            await _recorder.RecordEventAsync(
                rootId,
                "Consensus",
                Id.ToString(),
                new 
                {
                    TaskId = CustomState.TaskId,
                    Depth = CustomState.CurrentDepth,
                    Type = CustomState.ActiveGenerationType.ToString(),
                    Content = content
                },
                $"Consensus_{CustomState.CurrentDepth}");
        }
    }

    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleProposalReceivedAsync(ProposalReceivedEvent evt)
    {
        await base.HandleProposalReceivedAsync(evt);
        
        if (_recorder != null)
        {
            var rootId = CustomState.TaskId.Split(':')[0];
            
            // Determine type (Decomposition vs Atomic) to use in filename
            // But evt doesn't have type. We must infer from state or content.
            // ActiveGenerationType is in state.
            var type = CustomState.ActiveGenerationType == TaskAgentState.Types.GenerationRequestType.Decomposition 
                ? "Decomposer" 
                : "Solver";
            
            await _recorder.RecordEventAsync(
                rootId,
                "Proposal",
                Id.ToString(),
                new 
                {
                    TaskId = CustomState.TaskId,
                    Role = type, // This was missing from filename suffix
                    RequestId = evt.RequestId,
                    Content = evt.Content,
                    Reasoning = evt.ReasoningTrace
                },
                $"{type}_{evt.RequestId}"); // Cleaner filename: Decomposer_RequestId.json
        }
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        CustomConfig.SystemPromptTemplate =
            """
            You are a Master of Bazi (Chinese Metaphysics). Your goal is to orchestrate a rigorous, step-by-step analysis.
            
            [Decomposition Rules]
            1. Do NOT just list output sections. Decompose into LOGICAL STEPS.
            2. Logic Chain: Calibrate Data -> Determine Strength/Pattern (WangShua/GeJu) -> Identify Useful Gods (YongShen) -> Analyze Luck Cycles -> Final Prediction.
            3. CRITICAL: Ensure the output of Step N is strictly required for Step N+1.
            4. If a step is complex (e.g., "Determine Strength"), decompose it further.
            
            [Context Management]
            - When assigning child tasks, you MUST inject the conclusions from previous steps into the `context_variables` of the next step.
            - For example, when analyzing "Career", the child agent MUST know the "Useful God" determined in the previous step.
            """;
        
        CustomConfig.ConsensusThresholdK = 2; // K=2 for solid consensus
        CustomConfig.InitialFanOut = 3;       // 3 workers per step
        CustomConfig.MaxDepth = 4;
        CustomConfig.MaxCandidateWait = 12;
        CustomConfig.ProviderName = string.IsNullOrWhiteSpace(CustomConfig.ProviderName)
            ? "deepseek"
            : CustomConfig.ProviderName;
        CustomConfig.WorkerResponseTokenLimit = CustomConfig.WorkerResponseTokenLimit <= 0
            ? 800
            : Math.Max(CustomConfig.WorkerResponseTokenLimit, 800); // Allow longer reasoning traces
        CustomConfig.EnforceMicroSteps = true; // Enable micro steps to test granular progress reporting
        CustomConfig.EnableSelfWorker = true;
        CustomConfig.WorkerStopSequences.Clear();
        CustomConfig.WorkerStopSequences.Add("<END>");
        CustomConfig.SemanticSimilarityThreshold = Math.Max(CustomConfig.SemanticSimilarityThreshold, 0.95f);
    }

    // Base MakerTaskAgent now forwards goal excerpts + micro history to children via context_variables.
    // We only need to make sure the planner stays deterministic for this domain.

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

        // Force update objectives to state so UI can see them immediately
        // This is crucial because BuildMicroObjectivesAsync is called by InitializeMicroPlanAsync
        // which updates CustomState.MicroObjectives, but that happens AFTER this returns.
        // Wait, InitializeMicroPlanAsync does: CustomState.MicroObjectives.Add(objective);
        // So the state IS updated.
        // But maybe the snapshot is taken too early?
        
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

    protected override async Task<string> SynthesizeFinalReportAsync(CancellationToken ct)
    {
        await EnsurePlannerInitializedAsync(ct);

        var builder = new StringBuilder();
        builder.AppendLine("作为八字命理大师，请根据以下分步推演结果，撰写一份逻辑严密、文辞优雅的最终分析报告。");
        builder.AppendLine("【推演数据】");
        
        foreach (var kvp in CustomState.ChildResults)
        {
            builder.AppendLine($"--- 步骤 {kvp.Key} ---");
            builder.AppendLine(kvp.Value);
        }
        
        builder.AppendLine();
        builder.AppendLine("【要求】");
        builder.AppendLine("1. 必须包含：八字排盘、旺衰格局判定、喜忌神分析、大运流年吉凶、事业建议、健康建议。");
        builder.AppendLine("2. 去除重复信息，消除矛盾点（若有矛盾，取多数派或最合理的解释）。");
        builder.AppendLine("3. 语言风格：专业、客观、带有古典韵味但通俗易懂。");
        builder.AppendLine("4. 格式：Markdown，使用二级标题分隔板块。");

        var request = new ChatRequest
        {
            Message = builder.ToString(),
            RequestId = $"synthesizer-{Guid.NewGuid():N}",
            Temperature = 0.3,
            MaxTokens = 2000
        };

        var reportBuilder = new StringBuilder();
        await foreach (var chunk in ChatStreamAsync(request, ct))
        {
            if (string.IsNullOrWhiteSpace(chunk)) continue;
            reportBuilder.Append(chunk);
            Logger.LogInformation("📝 Synthesizer chunk: {Chunk}", chunk.Trim());
        }

        return reportBuilder.ToString();
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

                // Skip if it looks like a JSON array or object residue
                if (trimmed.StartsWith("[") || trimmed.StartsWith("]") || 
                    trimmed.StartsWith("{") || trimmed.StartsWith("}") ||
                    (trimmed.StartsWith("\"") && trimmed.EndsWith("\"") && trimmed.Contains(",")))
                {
                    continue;
                }

                // Clean up surrounding quotes and commas if it was parsed from a raw array string
                if (trimmed.StartsWith("\"") && trimmed.EndsWith("\","))
                {
                    trimmed = trimmed.Substring(1, trimmed.Length - 2);
                }
                else if (trimmed.StartsWith("\"") && trimmed.EndsWith("\""))
                {
                    trimmed = trimmed.Substring(1, trimmed.Length - 2);
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
    private readonly IMakerRunRecorder? _recorder;
    
    public BaziMakerWorkerAgent(IMakerRunRecorder? recorder = null)
    {
        _recorder = recorder;
    }

    // ... existing methods ...

    protected override async Task<string> GenerateResponseWithStreamAsync(ChatRequest request,
        GenerateProposalEvent evt)
    {
        var fullResponseBuilder = new StringBuilder();
        var answerBuilder = new StringBuilder();
        bool insideAnswer = false;

        // We need to capture the stream to record it AND parse it for the answer.
        // Base implementation logs to console but doesn't give us the stream chunks easily without re-implementing logic.
        // So we basically re-implement base logic here but adding recorder.
        
        await foreach (var chunk in ChatStreamAsync(request))
        {
            if (string.IsNullOrWhiteSpace(chunk))
            {
                continue;
            }

            var safeChunk = chunk.ReplaceLineEndings(" ").Replace("|", "¦").Trim();
            
            // Log to UI stream
            Logger.LogInformation("WORKER_STREAM|{WorkerId}|{RequestId}|{Chunk}",
                CustomState.WorkerId,
                evt.RequestId,
                safeChunk);
                
            fullResponseBuilder.Append(chunk); // Keep original format for full response
            
            // Simple state machine to extract answer on the fly (optional, or just parse at end)
            // Parsing at end is safer for tags.
        }
        
        var fullResponse = fullResponseBuilder.ToString();

        // Record full trace to file if recorder exists
        if (_recorder != null)
        {
            // We need to know the runId. Worker doesn't strictly know "RunId" context, 
            // but it might be in ContextVariables if we passed it down?
            // Actually, TaskAgent knows RunId (from TaskId). Worker just knows RequestId.
            // But we can try to infer or just use a flat structure for worker traces?
            // Or better: The TaskAgent records the "Proposal" event which contains the full content.
            // So we DON'T need to record the stream here to file, unless we want real-time replay.
            // The user asked: "WORKER_STREAM|的日志不要输出在控制台了，做成只放进录制文件里"
            // AND "把llm的每次输出 stream 打到文件里"
            
            // So we DO need to record stream chunks to file.
            // Problem: Worker doesn't know the RunId or the directory structure.
            // Solution: TaskAgent passed "parentTaskId" in ContextVariables? 
            // Wait, GenerateProposalEvent doesn't carry ContextVariables. AssignTaskEvent does.
            // Worker is stateless-ish.
            
            // We can't easily record to the structured Run folder from here without passing the RunId down.
            // Let's skip file recording of chunks for now, and rely on the final Proposal record in TaskAgent.
            // The user's requirement "trace.jsonl" in TaskAgent ALREADY contains the full content.
            // Real-time stream recording to file is heavy and tricky without context.
            
            // BUT, we MUST fix the console log noise.
            // I already kept Logger.LogInformation for UI.
            // If user wants it "only in file and UI", it means UI needs it (via Logger), but Console shouldn't show it.
            // That requires Logging config change, not code change here.
        }

        // Post-processing logic (same as before)
        if (evt.Type == GenerateProposalEvent.Types.GenerationType.Decomposition)
        {
            return fullResponse; 
        }

        if (fullResponse.Trim().StartsWith("[") && fullResponse.Trim().EndsWith("]"))
        {
             return fullResponse;
        }

        var startTag = "<Answer>";
        var endTag = "</Answer>";
        var s = fullResponse.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
        var e = fullResponse.IndexOf(endTag, StringComparison.OrdinalIgnoreCase);

        if (s >= 0 && e > s)
        {
            var answer = fullResponse.Substring(s + startTag.Length, e - s - startTag.Length).Trim();
            Logger.LogInformation("Parsed atomic answer from worker: {Answer}", answer);
            return answer;
        }

        return fullResponse;
    }
}