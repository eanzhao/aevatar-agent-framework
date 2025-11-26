using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Maker;
using MakerBaziDemo;

namespace MakerPaperSummaryDemo;

/// <summary>
/// Specific Task Agent for decomposing the paper summary task.
/// </summary>
public class PaperSummaryTaskAgent : MakerTaskAgent
{
    private readonly IMakerRunRecorder? _recorder;

    public PaperSummaryTaskAgent(IMakerChildLinker? childLinker, IMakerRunRecorder? recorder = null) : base(childLinker)
    {
        _recorder = recorder;
    }
    
    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        
        // Override system prompt to focus on paper summarization and decomposition
        CustomConfig.SystemPromptTemplate = """
You are the Editor-in-Chief of a scientific journal. 
Your goal is to oversee the creation of a comprehensive summary of the paper provided.
- When asked to DECOMPOSE: Break the paper down into its major logical sections (e.g., Introduction, Methods, Experiments). Output a JSON array of steps.
- When asked to SOLVE (Atomic): Provide a high-level synthesis of the section summaries provided by your workers.
- Ensure the final output is a coherent markdown document.
""";
        
        // Force max depth to 1 to ensure we only have 1 level of decomposition (Root -> Sections)
        // If we allowed deeper, the sections might be further decomposed, which is cool but maybe overkill for this demo.
        CustomConfig.MaxDepth = 1;
        
        SystemPrompt = CustomConfig.SystemPromptTemplate;
    }

    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleProposalReceivedAsync(ProposalReceivedEvent evt)
    {
        await base.HandleProposalReceivedAsync(evt);

        if (_recorder == null || string.IsNullOrWhiteSpace(CustomState.TaskId))
        {
            return;
        }

        var rootId = CustomState.TaskId.Split(':')[0];
        var role = CustomState.ActiveGenerationType == TaskAgentState.Types.GenerationRequestType.Decomposition
            ? "Planner"
            : "Summarizer";

        await _recorder.RecordEventAsync(
            rootId,
            "Proposal",
            Id.ToString(),
            new
            {
                TaskId = CustomState.TaskId,
                Depth = CustomState.CurrentDepth,
                Role = role,
                RequestId = evt.RequestId,
                Content = evt.Content,
                Reasoning = evt.ReasoningTrace
            },
            $"{role}_{evt.RequestId}");
    }

    protected override async Task OnConsensusReachedAsync(string content, CancellationToken ct)
    {
        await base.OnConsensusReachedAsync(content, ct);

        if (_recorder == null || string.IsNullOrWhiteSpace(CustomState.TaskId))
        {
            return;
        }

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
            $"Consensus_{CustomState.CurrentDepth:D2}");
    }
    
    // Override to produce a better final report from child results
    protected override async Task<string> SynthesizeFinalReportAsync(CancellationToken ct)
    {
        if (CustomState.ChildResults.Count == 0)
        {
            return await base.SynthesizeFinalReportAsync(ct);
        }

        try
        {
            var final = new StringBuilder();
            final.AppendLine($"# Summary: {PaperContent.Title}");
            final.AppendLine();

            var childByStep = CustomState.ChildResults
                .OrderBy(kvp => kvp.Key)
                .ToDictionary(kvp => ExtractStepCode(kvp.Key), kvp => CleanSectionText(kvp.Value));

            var appended = new HashSet<string>();

            foreach (var plan in CustomState.PlannedSteps)
            {
                var normalized = NormalizeStepId(plan.StepId);
                if (string.IsNullOrEmpty(normalized) || !childByStep.TryGetValue(normalized, out var content))
                {
                    continue;
                }

                AppendSection(final, normalized, plan.Description, content);
                appended.Add(normalized);
            }

            foreach (var kvp in childByStep)
            {
                if (appended.Contains(kvp.Key))
                {
                    continue;
                }

                AppendSection(final, kvp.Key, $"Additional Analysis {kvp.Key}", kvp.Value);
            }

            var summary = final.ToString();

            if (_recorder != null &&
                !string.IsNullOrWhiteSpace(CustomState.TaskId) &&
                CustomState.CurrentDepth == 0)
            {
                var rootId = CustomState.TaskId.Split(':')[0];
                await _recorder.RecordEventAsync(
                    rootId,
                    "Summary",
                    Id.ToString(),
                    new
                    {
                        TaskId = CustomState.TaskId,
                        FinalReport = summary
                    },
                    "Summary_Final");
            }

            return summary;
        }
        catch
        {
            return await base.SynthesizeFinalReportAsync(ct);
        }
    }

    private static void AppendSection(StringBuilder builder, string stepId, string description, string content)
    {
        var heading = string.IsNullOrWhiteSpace(description)
            ? $"## {stepId}"
            : $"## {stepId} · {description.Trim()}";

        builder.AppendLine(heading.Trim());
        builder.AppendLine();
        builder.AppendLine(content);
        builder.AppendLine();
    }

    private static string ExtractStepCode(string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return string.Empty;
        }

        var segments = taskId.Split(':', StringSplitOptions.RemoveEmptyEntries);
        var lastSegment = segments.LastOrDefault() ?? taskId;
        return NormalizeStepId(lastSegment);
    }

    private static string NormalizeStepId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var trimmed = raw.Trim();
        var match = Regex.Match(trimmed, @"^S?\s*(\d+)$", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            if (int.TryParse(match.Groups[1].Value, out var number))
            {
                return $"S{number:D2}";
            }
        }

        return trimmed.ToUpperInvariant();
    }

    private static readonly Regex SummaryHeaderRegex =
        new(@"^#{1,3}\s*summary\b.*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string CleanSectionText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "[empty]";
        }

        var normalized = raw.ReplaceLineEndings("\n");
        var lines = normalized.Split('\n').ToList();

        while (lines.Count > 0 && SummaryHeaderRegex.IsMatch(lines[0].Trim()))
        {
            lines.RemoveAt(0);
        }

        if (lines.Count == 0)
        {
            return "[empty]";
        }

        var combined = string.Join("\n", lines);
        combined = Regex.Replace(combined, @"[ \t]+\n", "\n");
        combined = Regex.Replace(combined, @"\n{3,}", "\n\n");

        return combined.Trim();
    }
}

/// <summary>
/// Worker Agent that reads the paper content and summarizes specific sections.
/// </summary>
public class PaperSummaryWorkerAgent : MakerWorkerAgent
{
    protected override (string prompt, string role) BuildPrompt(GenerateProposalEvent evt)
    {
        var builder = new StringBuilder();
        string role;

        if (evt.Type == GenerateProposalEvent.Types.GenerationType.Decomposition)
        {
            role = "planner";
            builder.AppendLine("You are a Senior Editor. Your task is to decompose the following paper into 5-6 distinct, logical sections for summarization.");
            builder.AppendLine("The output must be a JSON array where each item is { \"step_id\": \"S1\", \"description\": \"Summarize Section X: [Title]...\" }.");
            builder.AppendLine("Ensure the descriptions are specific enough for a worker to know which part of the text to read.");
            builder.AppendLine();
            builder.AppendLine("--- PAPER CONTENT (Condensed) ---");
            builder.AppendLine(PaperContent.FullText);
            builder.AppendLine("--- END CONTENT ---");
            builder.AppendLine();
            builder.AppendLine("Goal:");
        }
        else
        {
            role = "summarizer";
            builder.AppendLine("You are a Research Assistant. Your task is to summarize the specific section requested.");
            builder.AppendLine("1. Read the task description to identify the target section.");
            builder.AppendLine("2. Locate that section in the provided paper text.");
            builder.AppendLine("3. Write a concise but comprehensive summary (approx 100-150 words) of that section.");
            builder.AppendLine("4. Output valid Markdown, but DO NOT use a global \"# Summary\" heading.");
            builder.AppendLine("5. If you need headings, use at most level-3 (e.g., ### Key Ideas) so the orchestrator can merge multiple sections cleanly.");
            builder.AppendLine();
            builder.AppendLine("--- PAPER CONTENT (Condensed) ---");
            builder.AppendLine(PaperContent.FullText);
            builder.AppendLine("--- END CONTENT ---");
            builder.AppendLine();
            builder.AppendLine("Task:");
        }

        builder.AppendLine(evt.TaskDescription);
        return (builder.ToString(), role);
    }
}

