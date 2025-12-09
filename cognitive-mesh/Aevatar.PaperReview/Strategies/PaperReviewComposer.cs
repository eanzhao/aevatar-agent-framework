using System.Text;
using Aevatar.Agents.Maker;

namespace Aevatar.PaperReview.Strategies;

// ============================================================
//  Paper Review Composer
//  
//  Composes final review from all dimension consensus results.
//  Since each dimension already achieved consensus through MAKER,
//  composition is mostly assembly with minimal validation.
// ============================================================

/// <summary>
/// Paper review composition strategy.
/// Assembles final review from dimension-level consensus results.
/// </summary>
public sealed class PaperReviewComposer : ICompositionStrategy
{
    private readonly string _paperTitle;
    private readonly string _venueType;
    private readonly string _reviewType;

    public PaperReviewComposer(string paperTitle, string venueType, string reviewType)
    {
        _paperTitle = paperTitle;
        _venueType = venueType;
        _reviewType = reviewType;
    }

    /// <inheritdoc />
    public string? Compose(
        string originalTask,
        IReadOnlyDictionary<string, string> subtaskResults,
        IReadOnlyDictionary<string, string> context)
    {
        // Since dimension results are already consensus-validated,
        // we can compose directly without additional LLM synthesis
        // This is the key insight: no validation needed for voted content
        
        if (subtaskResults.Count == 0)
        {
            return null; // Trigger LLM synthesis as fallback
        }

        return ComposeDirectly(subtaskResults);
    }

    /// <inheritdoc />
    public string BuildSynthesisPrompt(
        string originalTask,
        IReadOnlyDictionary<string, string> subtaskResults,
        IReadOnlyDictionary<string, string> context)
    {
        var resultsSection = new StringBuilder();
        foreach (var (dimId, result) in subtaskResults.OrderBy(kv => kv.Key))
        {
            resultsSection.AppendLine($"### Dimension {dimId}");
            resultsSection.AppendLine(result);
            resultsSection.AppendLine();
            resultsSection.AppendLine("---");
            resultsSection.AppendLine();
        }

        return $$"""
            You are the Senior Area Chair synthesizing a final review from multiple expert dimension reviews.
            
            ═══════════════════════════════════════════════════════════════
            TASK: Synthesize Final Review
            ═══════════════════════════════════════════════════════════════
            
            Paper: {{_paperTitle}}
            Venue: {{_venueType}}
            Review Depth: {{_reviewType}}
            
            Below are the expert reviews for each dimension. Your task is to:
            1. Preserve ALL specific feedback from each dimension (do NOT lose detail)
            2. Add a concise executive summary at the top
            3. Compute an overall recommendation based on dimension scores
            4. Ensure logical flow between sections
            
            ═══════════════════════════════════════════════════════════════
            DIMENSION REVIEWS (Consensus-Validated)
            ═══════════════════════════════════════════════════════════════
            
            {{resultsSection}}
            
            ═══════════════════════════════════════════════════════════════
            OUTPUT FORMAT
            ═══════════════════════════════════════════════════════════════
            
            # Paper Review: {{_paperTitle}}
            
            ## Executive Summary
            [2-3 sentences: core contribution + overall assessment]
            
            [Include all dimension reviews in order, preserving their content]
            
            ## Overall Recommendation
            - **Recommendation**: Strong Accept / Accept / Weak Accept / Borderline / Weak Reject / Reject / Strong Reject
            - **Confidence**: X/5
            - **Justification**: [Based on dimension scores and critical issues]
            
            ═══════════════════════════════════════════════════════════════
            """;
    }

    // ─────────────────────────────────────────────────────────
    //  Direct Composition (No LLM)
    // ─────────────────────────────────────────────────────────

    private string ComposeDirectly(IReadOnlyDictionary<string, string> results)
    {
        var sb = new StringBuilder();
        
        // Header
        sb.AppendLine($"# Paper Review: {_paperTitle}");
        sb.AppendLine();
        sb.AppendLine($"**Venue**: {_venueType}");
        sb.AppendLine($"**Review Depth**: {_reviewType}");
        sb.AppendLine($"**Dimensions Evaluated**: {results.Count}");
        sb.AppendLine();
        
        // Executive Summary placeholder (will be filled by LLM if needed)
        sb.AppendLine("## Executive Summary");
        sb.AppendLine();
        sb.AppendLine("_This review evaluates the paper across multiple dimensions, each validated through multi-expert consensus._");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        
        // Dimension reviews
        var scores = new List<int>();
        
        foreach (var (dimId, result) in results.OrderBy(kv => kv.Key))
        {
            sb.AppendLine(result.Trim());
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            
            // Try to extract score
            var score = ExtractScore(result);
            if (score > 0)
            {
                scores.Add(score);
            }
        }
        
        // Overall recommendation
        sb.AppendLine("## Overall Assessment");
        sb.AppendLine();
        
        if (scores.Count > 0)
        {
            var avgScore = scores.Average();
            var recommendation = avgScore switch
            {
                >= 4.5 => "Strong Accept",
                >= 4.0 => "Accept",
                >= 3.5 => "Weak Accept",
                >= 3.0 => "Borderline",
                >= 2.5 => "Weak Reject",
                >= 2.0 => "Reject",
                _ => "Strong Reject"
            };
            
            sb.AppendLine($"**Average Dimension Score**: {avgScore:F1}/5");
            sb.AppendLine($"**Recommendation**: {recommendation}");
            sb.AppendLine();
            sb.AppendLine($"_Based on {scores.Count} dimension scores: {string.Join(", ", scores.Select(s => $"{s}/5"))}_");
        }
        else
        {
            sb.AppendLine("**Recommendation**: See individual dimension assessments above.");
        }
        
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("_This review was generated using the MAKER multi-expert consensus system. Each dimension was reviewed independently by multiple AI experts, with consensus achieved through voting._");
        
        return sb.ToString();
    }

    private static int ExtractScore(string content)
    {
        // Look for patterns like "Score: 4/5" or "### Score: 3/5"
        var lines = content.Split('\n');
        foreach (var line in lines)
        {
            var lower = line.ToLowerInvariant();
            if (lower.Contains("score") && lower.Contains("/5"))
            {
                // Extract the number before /5
                var idx = lower.IndexOf("/5", StringComparison.Ordinal);
                if (idx > 0)
                {
                    // Look backwards for a digit
                    for (int i = idx - 1; i >= 0; i--)
                    {
                        if (char.IsDigit(line[i]))
                        {
                            if (int.TryParse(line[i].ToString(), out var score) && score >= 1 && score <= 5)
                            {
                                return score;
                            }
                        }
                        else if (!char.IsWhiteSpace(line[i]))
                        {
                            break;
                        }
                    }
                }
            }
        }
        
        return 0;
    }
}
