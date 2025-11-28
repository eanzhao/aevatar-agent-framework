using Shouldly;
using Xunit;

namespace Aevatar.Agents.Maker.Tests;

// ============================================================
//  DefaultComposer Tests - Result Aggregation
//  Paper Reference: Composition of subtask results
// ============================================================

public class DefaultComposerTests
{
    private readonly DefaultComposer _composer = new();

    // ============================================================
    //  Compose Tests - Simple Aggregation
    // ============================================================

    [Fact(DisplayName = "Small results use simple aggregation")]
    public void Compose_SmallResults_ShouldReturnSimpleAggregation()
    {
        var results = new Dictionary<string, string>
        {
            ["S1"] = "First result",
            ["S2"] = "Second result"
        };

        var composed = _composer.Compose("Original task", results, new Dictionary<string, string>());

        composed.ShouldNotBeNull();
        composed.ShouldContain("## S1");
        composed.ShouldContain("First result");
        composed.ShouldContain("## S2");
        composed.ShouldContain("Second result");
    }

    [Fact(DisplayName = "Results ordered by step ID")]
    public void Compose_ResultsOrderedByStepId()
    {
        var results = new Dictionary<string, string>
        {
            ["S3"] = "Third",
            ["S1"] = "First",
            ["S2"] = "Second"
        };

        var composed = _composer.Compose("Task", results, new Dictionary<string, string>());

        composed.ShouldNotBeNull();
        var s1Index = composed.IndexOf("S1");
        var s2Index = composed.IndexOf("S2");
        var s3Index = composed.IndexOf("S3");

        s1Index.ShouldBeLessThan(s2Index);
        s2Index.ShouldBeLessThan(s3Index);
    }

    [Fact(DisplayName = "Large results return null to trigger LLM synthesis")]
    public void Compose_LargeResults_ShouldReturnNullForLLMSynthesis()
    {
        // Create results exceeding the threshold (default 2000 chars)
        var largeResult = new string('x', 1500);
        var results = new Dictionary<string, string>
        {
            ["S1"] = largeResult,
            ["S2"] = largeResult
        };

        var composed = _composer.Compose("Task", results, new Dictionary<string, string>());

        composed.ShouldBeNull(); // Triggers LLM synthesis
    }

    [Fact(DisplayName = "Many subtasks return null to trigger LLM synthesis")]
    public void Compose_ManySubtasks_ShouldReturnNullForLLMSynthesis()
    {
        // More than 3 subtasks should trigger LLM synthesis
        var results = new Dictionary<string, string>
        {
            ["S1"] = "Result 1",
            ["S2"] = "Result 2",
            ["S3"] = "Result 3",
            ["S4"] = "Result 4"
        };

        var composed = _composer.Compose("Task", results, new Dictionary<string, string>());

        composed.ShouldBeNull();
    }

    [Fact(DisplayName = "Exactly 3 subtasks use simple aggregation")]
    public void Compose_ExactlyThreeSubtasks_ShouldSimpleAggregate()
    {
        var results = new Dictionary<string, string>
        {
            ["S1"] = "One",
            ["S2"] = "Two",
            ["S3"] = "Three"
        };

        var composed = _composer.Compose("Task", results, new Dictionary<string, string>());

        composed.ShouldNotBeNull();
    }

    [Fact(DisplayName = "Custom threshold is respected")]
    public void Compose_CustomThreshold_ShouldRespect()
    {
        var customComposer = new DefaultComposer { SimpleAggregationThreshold = 100 };
        var results = new Dictionary<string, string>
        {
            ["S1"] = new string('x', 60),
            ["S2"] = new string('y', 60)
        };

        var composed = customComposer.Compose("Task", results, new Dictionary<string, string>());

        composed.ShouldBeNull(); // 120 > 100 threshold
    }

    // ============================================================
    //  BuildSynthesisPrompt Tests
    // ============================================================

    [Fact(DisplayName = "Synthesis prompt includes original task")]
    public void BuildSynthesisPrompt_ShouldIncludeOriginalTask()
    {
        var results = new Dictionary<string, string>
        {
            ["S1"] = "Result 1"
        };

        var prompt = _composer.BuildSynthesisPrompt(
            "Analyze the system",
            results,
            new Dictionary<string, string>());

        prompt.ShouldContain("[Original Task]");
        prompt.ShouldContain("Analyze the system");
    }

    [Fact(DisplayName = "Synthesis prompt includes all subtask results")]
    public void BuildSynthesisPrompt_ShouldIncludeAllSubtaskResults()
    {
        var results = new Dictionary<string, string>
        {
            ["S1"] = "First analysis complete",
            ["S2"] = "Second analysis complete",
            ["S3"] = "Third analysis complete"
        };

        var prompt = _composer.BuildSynthesisPrompt(
            "Task",
            results,
            new Dictionary<string, string>());

        prompt.ShouldContain("[Subtask Results]");
        prompt.ShouldContain("--- S1 ---");
        prompt.ShouldContain("First analysis complete");
        prompt.ShouldContain("--- S2 ---");
        prompt.ShouldContain("Second analysis complete");
        prompt.ShouldContain("--- S3 ---");
        prompt.ShouldContain("Third analysis complete");
    }

    [Fact(DisplayName = "Synthesis prompt orders results by step ID")]
    public void BuildSynthesisPrompt_ResultsOrderedByStepId()
    {
        var results = new Dictionary<string, string>
        {
            ["S3"] = "Third",
            ["S1"] = "First",
            ["S2"] = "Second"
        };

        var prompt = _composer.BuildSynthesisPrompt(
            "Task",
            results,
            new Dictionary<string, string>());

        var s1Index = prompt.IndexOf("--- S1 ---");
        var s2Index = prompt.IndexOf("--- S2 ---");
        var s3Index = prompt.IndexOf("--- S3 ---");

        s1Index.ShouldBeLessThan(s2Index);
        s2Index.ShouldBeLessThan(s3Index);
    }

    [Fact(DisplayName = "Synthesis prompt includes instructions")]
    public void BuildSynthesisPrompt_ShouldIncludeInstructions()
    {
        var results = new Dictionary<string, string>
        {
            ["S1"] = "Result"
        };

        var prompt = _composer.BuildSynthesisPrompt(
            "Task",
            results,
            new Dictionary<string, string>());

        prompt.ShouldContain("[Instructions]");
        prompt.ShouldContain("coherent");
        prompt.ShouldContain("redundancy");
    }

    [Fact(DisplayName = "Synthesis prompt ends with output marker")]
    public void BuildSynthesisPrompt_ShouldEndWithOutputMarker()
    {
        var results = new Dictionary<string, string>
        {
            ["S1"] = "Result"
        };

        var prompt = _composer.BuildSynthesisPrompt(
            "Task",
            results,
            new Dictionary<string, string>());

        prompt.ShouldContain("[Synthesized Output]");
    }

    // ============================================================
    //  Edge Cases
    // ============================================================

    [Fact(DisplayName = "Empty results returns empty string")]
    public void Compose_EmptyResults_ShouldReturnEmpty()
    {
        var results = new Dictionary<string, string>();

        var composed = _composer.Compose("Task", results, new Dictionary<string, string>());

        composed.ShouldNotBeNull();
        composed.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Single result returns formatted output")]
    public void Compose_SingleResult_ShouldReturnFormatted()
    {
        var results = new Dictionary<string, string>
        {
            ["S1"] = "Only result"
        };

        var composed = _composer.Compose("Task", results, new Dictionary<string, string>());

        composed.ShouldNotBeNull();
        composed.ShouldContain("## S1");
        composed.ShouldContain("Only result");
    }

    [Fact(DisplayName = "Results with whitespace are trimmed")]
    public void Compose_ResultsWithWhitespace_ShouldTrim()
    {
        var results = new Dictionary<string, string>
        {
            ["S1"] = "  Result with spaces  "
        };

        var composed = _composer.Compose("Task", results, new Dictionary<string, string>());

        composed.ShouldNotBeNull();
        composed.ShouldContain("Result with spaces");
        composed.ShouldNotContain("  Result");
    }
}
