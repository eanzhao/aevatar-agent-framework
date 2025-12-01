using System.Numerics;
using System.Text;
using System.Text.Json;
using Aevatar.Agents.CreativeReasoning.Messages;

namespace Aevatar.Agents.CreativeReasoning.Strategies;

/// <summary>
/// Default implementation of donor selection using Far-then-Analogical method.
/// Prioritizes semantically distant donors for creative novelty.
/// </summary>
public class DefaultDonorSelectionStrategy : IDonorSelectionStrategy
{
    public string BuildDonorSelectionPrompt(
        ThoughtUnit originalThought,
        IReadOnlyList<ThoughtUnit> candidateDonors,
        string originalProblem,
        float farDistanceThreshold)
    {
        var donorList = new StringBuilder();
        for (var i = 0; i < candidateDonors.Count; i++)
        {
            var donor = candidateDonors[i];
            donorList.AppendLine($"""
                ### Donor {i + 1}: {donor.Id}
                Type: {donor.Type}
                Source: {donor.SourceProblem}
                Content: {donor.Content}
                Semantic Distance: {donor.SemanticDistance:F2}
                """);
        }

        return $$"""
            # Task: Select Donor Thought for Creative Substitution

            ## Original Problem
            {{originalProblem}}

            ## Thought to Replace
            ID: {{originalThought.Id}}
            Type: {{originalThought.Type}}
            Content: {{originalThought.Content}}

            ## Candidate Donors (sorted by semantic distance - farther first)
            {{donorList}}

            ## Far-then-Analogical Selection Criteria
            1. PREFER donors with HIGH semantic distance (novelty)
            2. BUT ensure analogical relevance (functional similarity)
            3. The substitution should create something that COULD work for the original problem

            ## Output Format (JSON object)
            ```json
            {
              "donor_thought_id": "thought_id",
              "rationale": "Why this donor creates valuable novelty while maintaining functional relevance",
              "estimated_novelty": 0.8
            }
            ```

            Output ONLY the JSON object, no additional text.
            """;
    }

    public DonorSelectionResult ParseDonorSelection(string llmOutput)
    {
        var json = ExtractJsonObject(llmOutput);
        
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            var donorId = root.GetProperty("donor_thought_id").GetString() ?? "";
            var rationale = root.TryGetProperty("rationale", out var r) ? r.GetString() ?? "" : "";
            var novelty = root.TryGetProperty("estimated_novelty", out var n) ? n.GetSingle() : 0.7f;

            return new DonorSelectionResult(donorId, rationale, novelty);
        }
        catch (JsonException)
        {
            return new DonorSelectionResult("", "Parsing failed", 0.5f);
        }
    }

    public IReadOnlyList<ThoughtUnit> FilterByDistance(
        ThoughtUnit target,
        IReadOnlyList<ThoughtUnit> candidates,
        float farThreshold,
        int maxCount)
    {
        // If target has embedding, compute distances
        if (target.Embedding.Count > 0)
        {
            var withDistances = candidates
                .Where(c => c.Embedding.Count > 0 && c.Id != target.Id)
                .Select(c => (Thought: c, Distance: 1 - CosineSimilarity(target.Embedding, c.Embedding)))
                .OrderByDescending(x => x.Distance) // Farthest first
                .ToList();

            // Take far donors first (above threshold), then fill with closer ones
            var farDonors = withDistances
                .Where(x => x.Distance >= farThreshold)
                .Take(maxCount)
                .ToList();

            if (farDonors.Count < maxCount)
            {
                var remaining = maxCount - farDonors.Count;
                var closerDonors = withDistances
                    .Where(x => x.Distance < farThreshold)
                    .Take(remaining);
                farDonors.AddRange(closerDonors);
            }

            // Update semantic distance in returned thoughts
            return farDonors.Select(x =>
            {
                var t = x.Thought;
                t.SemanticDistance = x.Distance;
                return t;
            }).ToList();
        }

        // No embeddings: return candidates from different source problems
        return candidates
            .Where(c => c.SourceProblem != target.SourceProblem)
            .Take(maxCount)
            .ToList();
    }

    private static float CosineSimilarity(
        Google.Protobuf.Collections.RepeatedField<float> a,
        Google.Protobuf.Collections.RepeatedField<float> b)
    {
        if (a.Count != b.Count || a.Count == 0) return 0;

        float dot = 0, magA = 0, magB = 0;
        var simdLength = Vector<float>.Count;
        var length = a.Count;
        var simdEnd = length - (length % simdLength);

        // SIMD path
        var aArray = a.ToArray();
        var bArray = b.ToArray();

        for (var i = 0; i < simdEnd; i += simdLength)
        {
            var va = new Vector<float>(aArray, i);
            var vb = new Vector<float>(bArray, i);
            dot += Vector.Dot(va, vb);
            magA += Vector.Dot(va, va);
            magB += Vector.Dot(vb, vb);
        }

        // Scalar remainder
        for (var i = simdEnd; i < length; i++)
        {
            dot += aArray[i] * bArray[i];
            magA += aArray[i] * aArray[i];
            magB += bArray[i] * bArray[i];
        }

        var denom = MathF.Sqrt(magA) * MathF.Sqrt(magB);
        return denom == 0 ? 0 : dot / denom;
    }

    private static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }
}

