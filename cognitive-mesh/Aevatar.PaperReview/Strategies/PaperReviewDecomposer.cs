using System.Text.Json;
using Aevatar.Agents.Maker;

namespace Aevatar.PaperReview.Strategies;

// ============================================================
//  Paper Review Decomposer
//  
//  Two-Phase Architecture:
//  1. Analyze paper → understand domain, contribution, structure
//  2. Generate review dimensions tailored to THIS paper
//  
//  Each dimension becomes a subtask for MAKER consensus voting.
// ============================================================

/// <summary>
/// Paper review decomposition strategy.
/// First analyzes the paper, then generates appropriate review dimensions.
/// </summary>
public sealed class PaperReviewDecomposer : IDecompositionStrategy
{
    private readonly string _venueType;
    private readonly string _reviewType;
    private readonly string _paperContent;

    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    public PaperReviewDecomposer(string venueType, string reviewType, string paperContent)
    {
        _venueType = venueType;
        _reviewType = reviewType;
        _paperContent = paperContent;
    }

    /// <inheritdoc />
    public string BuildDecompositionPrompt(string taskDescription, IReadOnlyDictionary<string, string> context)
    {
        return BuildDecompositionPrompt(taskDescription, context, DecompositionGranularity.Balanced);
    }

    /// <inheritdoc />
    public string BuildDecompositionPrompt(
        string taskDescription, 
        IReadOnlyDictionary<string, string> context,
        DecompositionGranularity granularity)
    {
        var (minDims, maxDims, focusLevel) = _reviewType switch
        {
            "Quick" => (2, 3, "Focus on 2-3 most critical dimensions only."),
            "Standard" => (4, 5, "Cover 4-5 key dimensions for comprehensive review."),
            "Detailed" => (5, 6, "Thorough coverage of 5-6 dimensions with detailed focus."),
            "Rigorous" => (6, 7, "Rigorous analysis of 6-7 dimensions with deep scrutiny."),
            "Critical" => (7, 8, "Exhaustive analysis of all relevant dimensions."),
            _ => (4, 5, "Cover key dimensions appropriate for the venue.")
        };

        return $$"""
            You are a Senior Meta-Reviewer at a top-tier {{_venueType}}. 
            Your task is to analyze this paper and design a structured review plan.
            
            ═══════════════════════════════════════════════════════════════
            STEP 1: PAPER ANALYSIS
            ═══════════════════════════════════════════════════════════════
            
            First, understand the paper:
            - What is the core contribution?
            - What domain/field is this paper in?
            - What type of paper is this? (theoretical, empirical, systems, survey, etc.)
            - What are the key claims being made?
            
            ═══════════════════════════════════════════════════════════════
            STEP 2: DESIGN REVIEW DIMENSIONS
            ═══════════════════════════════════════════════════════════════
            
            {{focusLevel}}
            Design {{minDims}}-{{maxDims}} review dimensions TAILORED TO THIS SPECIFIC PAPER.
            
            Common dimensions (adapt based on paper type):
            
            FOR THEORETICAL PAPERS:
            - Proof Correctness: Mathematical rigor, logical consistency
            - Novelty of Results: Originality of theorems/lemmas
            - Tightness of Bounds: Are bounds tight? Can they be improved?
            - Clarity of Exposition: Proof readability, intuition provided
            
            FOR EMPIRICAL/ML PAPERS:
            - Technical Soundness: Method correctness, assumption validity
            - Experimental Design: Baselines, ablations, statistical significance
            - Novelty & Contribution: What's new vs. incremental
            - Reproducibility: Code, hyperparameters, compute requirements
            - Real-world Impact: Practical applicability
            
            FOR SYSTEMS PAPERS:
            - Design Choices: Architecture decisions, trade-offs
            - Scalability: Performance at scale, bottlenecks
            - Evaluation: Benchmarks, comparisons, real workloads
            - Practicality: Deployment considerations, limitations
            
            UNIVERSAL DIMENSIONS:
            - Presentation Quality: Writing, figures, organization
            - Related Work: Coverage, fair comparison, positioning
            - Broader Impact: Ethics, societal implications
            
            ═══════════════════════════════════════════════════════════════
            OUTPUT FORMAT (JSON Array)
            ═══════════════════════════════════════════════════════════════
            
            ```json
            [
              {
                "step_id": "D1",
                "description": "DIMENSION_NAME: Brief description of what to evaluate. FOCUS: specific aspects to check for THIS paper."
              },
              ...
            ]
            ```
            
            CRITICAL RULES:
            1. Each dimension MUST be reviewable independently
            2. Description MUST include FOCUS: tag with paper-specific details
            3. Order dimensions by importance (most critical first)
            4. Output ONLY the JSON array, no other text
            
            ═══════════════════════════════════════════════════════════════
            PAPER TO ANALYZE
            ═══════════════════════════════════════════════════════════════
            
            {{_paperContent[..Math.Min(_paperContent.Length, 50000)]}}
            
            ═══════════════════════════════════════════════════════════════
            
            Output ONLY the JSON array:
            """;
    }

    /// <inheritdoc />
    public bool IsAtomic(string taskDescription, int currentDepth)
    {
        // ─────────────────────────────────────────────────────────
        //  Two-Phase Architecture:
        //  - Depth 0: NOT atomic → decompose into review dimensions
        //  - Depth 1+: ATOMIC → solve with MAKER voting
        //  
        //  Each dimension (depth 1) becomes a separate solve task,
        //  which triggers independent MAKER voting for consensus.
        // ─────────────────────────────────────────────────────────
        return currentDepth >= 1;
    }

    /// <inheritdoc />
    public IReadOnlyList<(string StepId, string Description)> ParseDecomposition(string llmOutput)
    {
        var steps = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(llmOutput))
        {
            return GetFallbackDimensions();
        }

        var json = ExtractJsonArray(llmOutput);
        if (string.IsNullOrWhiteSpace(json))
        {
            return GetFallbackDimensions();
        }

        try
        {
            using var doc = JsonDocument.Parse(json, JsonOptions);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return GetFallbackDimensions();
            }

            var index = 1;
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                string? stepId = null;
                string? description = null;

                if (element.ValueKind == JsonValueKind.Object)
                {
                    stepId = element.TryGetProperty("step_id", out var sid) ? sid.GetString() : null;
                    description = element.TryGetProperty("description", out var desc) 
                        ? desc.GetString() 
                        : element.TryGetProperty("dimension", out var dim)
                            ? dim.GetString()
                            : null;
                }
                else if (element.ValueKind == JsonValueKind.String)
                {
                    description = element.GetString();
                }

                stepId = string.IsNullOrWhiteSpace(stepId) ? $"D{index}" : stepId.Trim();
                description = description?.Trim();

                if (!string.IsNullOrWhiteSpace(description))
                {
                    steps.Add((stepId, description));
                    index++;
                }
            }
        }
        catch (JsonException)
        {
            return GetFallbackDimensions();
        }

        return steps.Count > 0 ? steps : GetFallbackDimensions();
    }

    // ─────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────

    private static string ExtractJsonArray(string content)
    {
        // Try code block first
        var codeStart = content.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (codeStart >= 0)
        {
            var arrayStart = content.IndexOf('[', codeStart);
            var codeEnd = content.IndexOf("```", codeStart + 7);
            if (arrayStart >= 0 && codeEnd > arrayStart)
            {
                var arrayEnd = content.LastIndexOf(']', codeEnd);
                if (arrayEnd > arrayStart)
                {
                    return content[arrayStart..(arrayEnd + 1)];
                }
            }
        }

        // Raw JSON
        var start = content.IndexOf('[');
        var end = content.LastIndexOf(']');
        if (start >= 0 && end > start)
        {
            return content[start..(end + 1)];
        }

        return content.Trim();
    }

    private IReadOnlyList<(string, string)> GetFallbackDimensions()
    {
        // Fallback dimensions based on review type
        return _reviewType switch
        {
            "Quick" => new List<(string, string)>
            {
                ("D1", "Technical Soundness: Correctness of methods, proofs, algorithms. FOCUS: Major technical flaws only."),
                ("D2", "Contribution & Impact: Significance and novelty. FOCUS: Is this worth publishing?")
            },
            "Critical" or "Rigorous" => new List<(string, string)>
            {
                ("D1", "Technical Soundness: Mathematical rigor, logical consistency, correctness of all claims. FOCUS: Proof validity, assumption reasonableness."),
                ("D2", "Novelty & Contribution: Originality vs. incremental improvement. FOCUS: What exactly is new? How significant?"),
                ("D3", "Experimental Evaluation: Design, baselines, statistical validity. FOCUS: Are experiments convincing? Missing comparisons?"),
                ("D4", "Reproducibility: Code, data, implementation details. FOCUS: Can this be replicated?"),
                ("D5", "Presentation Quality: Writing, figures, organization. FOCUS: Clarity issues, confusing sections."),
                ("D6", "Related Work: Coverage, fair comparison. FOCUS: Missing references, unfair comparisons."),
                ("D7", "Broader Impact: Ethics, societal implications. FOCUS: Potential misuse, bias concerns.")
            },
            _ => new List<(string, string)>
            {
                ("D1", "Technical Soundness: Correctness of methods, proofs, algorithms. FOCUS: Identify any technical flaws."),
                ("D2", "Novelty & Contribution: Originality and significance. FOCUS: What's new? Why does it matter?"),
                ("D3", "Experimental Evaluation: Quality of experiments and validation. FOCUS: Baselines, metrics, significance."),
                ("D4", "Presentation Quality: Writing, figures, organization. FOCUS: Clarity and readability issues.")
            }
        };
    }
}
