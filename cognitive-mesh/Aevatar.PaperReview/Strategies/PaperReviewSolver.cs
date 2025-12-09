using System.Text.RegularExpressions;
using Aevatar.Agents.Maker;

namespace Aevatar.PaperReview.Strategies;

// ============================================================
//  Paper Review Solver
//  
//  Handles per-dimension deep review.
//  Each dimension gets specialized prompting for focused analysis.
// ============================================================

/// <summary>
/// Paper review solution strategy.
/// Generates deep, focused reviews for each dimension.
/// </summary>
public sealed partial class PaperReviewSolver : ISolutionStrategy
{
    private readonly string _paperContent;
    private readonly string _paperTitle;
    private readonly string _venueType;

    public PaperReviewSolver(string paperContent, string paperTitle, string venueType)
    {
        _paperContent = paperContent;
        _paperTitle = paperTitle;
        _venueType = venueType;
    }

    /// <inheritdoc />
    public string BuildSolvePrompt(string taskDescription, IReadOnlyDictionary<string, string> context)
    {
        // Parse dimension name and focus from taskDescription
        var (dimensionName, focus) = ParseDimensionInfo(taskDescription);
        
        return $$"""
            You are an Expert Reviewer for {{_venueType}}, specializing in: **{{dimensionName}}**
            
            ═══════════════════════════════════════════════════════════════
            YOUR SOLE TASK: Evaluate {{dimensionName}}
            ═══════════════════════════════════════════════════════════════
            
            {{taskDescription}}
            
            ═══════════════════════════════════════════════════════════════
            REVIEW GUIDELINES
            ═══════════════════════════════════════════════════════════════
            
            1. **Be Specific**: Cite exact sections, equations, figures, tables, line numbers
            2. **Be Constructive**: For each weakness, provide a concrete improvement suggestion
            3. **Be Fair**: Acknowledge strengths even when pointing out weaknesses
            4. **Be Expert**: Your assessment should reflect deep domain expertise
            5. **Stay Focused**: ONLY evaluate {{dimensionName}} - other aspects are handled separately
            
            ═══════════════════════════════════════════════════════════════
            OUTPUT FORMAT
            ═══════════════════════════════════════════════════════════════
            
            ## {{dimensionName}}
            
            ### Assessment
            [200-400 words of detailed evaluation. Reference specific parts of the paper.]
            
            ### Strengths
            - **[Strength Title]**: [Specific evidence from paper] (e.g., "Section 3.2 provides clear proof...")
            - **[Strength Title]**: [Specific evidence]
            - ...
            
            ### Weaknesses
            - **[Weakness Title]**: [Specific evidence] → **Suggestion**: [How to fix]
            - **[Weakness Title]**: [Specific evidence] → **Suggestion**: [How to fix]
            - ...
            
            ### Clarifying Questions
            - [Question that would help resolve uncertainty]
            - ...
            
            ### Score: X/5
            1 = Major issues, unacceptable
            2 = Significant concerns
            3 = Acceptable with revisions
            4 = Good, minor issues
            5 = Excellent
            
            [One sentence justification for the score]
            
            ═══════════════════════════════════════════════════════════════
            PAPER: {{_paperTitle}}
            ═══════════════════════════════════════════════════════════════
            
            {{_paperContent}}
            
            ═══════════════════════════════════════════════════════════════
            
            Remember: Focus ONLY on {{dimensionName}}. Be thorough but concise.
            """;
    }

    /// <inheritdoc />
    public string ExtractSolution(string llmOutput)
    {
        // Clean up the output but preserve markdown structure
        var result = llmOutput.Trim();
        
        // Remove any leading/trailing code blocks if LLM wrapped response
        if (result.StartsWith("```"))
        {
            var endIdx = result.IndexOf('\n');
            if (endIdx > 0)
            {
                result = result[(endIdx + 1)..];
            }
            if (result.EndsWith("```"))
            {
                result = result[..^3].TrimEnd();
            }
        }
        
        return result;
    }

    // ─────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────

    private static (string Name, string Focus) ParseDimensionInfo(string taskDescription)
    {
        // 1) 取首行，避免把整段 prompt 当成名称
        var firstLine = taskDescription.Split('\n').FirstOrDefault()?.Trim() ?? taskDescription;
        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return ("Unknown Dimension", "");
        }
        
        // 2) 如果存在 "FOCUS:" 段落，提取 focus
        var focusMatch = FocusPattern().Match(taskDescription);
        var focus = focusMatch.Success ? focusMatch.Groups[1].Value.Trim() : "";
        
        // 3) 若有冒号，冒号前是名称
        var colonIdx = firstLine.IndexOf(':');
        if (colonIdx > 0 && colonIdx < 80)
        {
            var name = firstLine[..colonIdx].Trim();
            name = SanitizeName(name);
            return (name, focus);
        }
        
        // 4) 否则，直接用首行做名称，并进行清洗
        var cleaned = SanitizeName(firstLine);
        if (cleaned.Length > 60) cleaned = cleaned[..60] + "...";
        return (cleaned, focus);
    }

    private static string SanitizeName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Unknown Dimension";
        
        var name = raw.Trim();
        
        // 去掉常见的角色前缀
        if (name.StartsWith("You are", StringComparison.OrdinalIgnoreCase))
        {
            name = name.Substring("You are".Length).TrimStart('.', ':', ',', ' ');
        }
        if (name.StartsWith("an", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("a ", StringComparison.OrdinalIgnoreCase))
        {
            // 防止 "an Expert Reviewer" 这样的前缀
            var parts = name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2) name = parts[1];
        }

        // 清理掉重叠的 ** 或 markdown 符号
        name = name.Trim('*', '-', ' ');
        if (string.IsNullOrWhiteSpace(name)) return "Unknown Dimension";
        return name;
    }

    [GeneratedRegex(@"FOCUS:\s*(.+?)(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex FocusPattern();
}
