using Shouldly;
using Xunit;

namespace Aevatar.Agents.Maker.Tests;

// ============================================================
//  DefaultSolver Tests - Atomic Task Solution
//  Paper Reference: Atomic task solving after maximal decomposition
// ============================================================

public class DefaultSolverTests
{
    private readonly DefaultSolver _solver = new();

    // ============================================================
    //  BuildSolvePrompt Tests
    // ============================================================

    [Fact(DisplayName = "Prompt excludes context section when empty")]
    public void BuildSolvePrompt_WithEmptyContext_ShouldNotIncludeContextSection()
    {
        var prompt = _solver.BuildSolvePrompt(
            "Calculate 2+2",
            new Dictionary<string, string>());

        prompt.ShouldContain("Calculate 2+2");
        prompt.ShouldNotContain("[Available Information]");
    }

    [Fact(DisplayName = "Prompt includes context section when provided")]
    public void BuildSolvePrompt_WithContext_ShouldIncludeContextSection()
    {
        var context = new Dictionary<string, string>
        {
            ["PreviousResult"] = "42",
            ["DataFormat"] = "JSON"
        };

        var prompt = _solver.BuildSolvePrompt("Process the data", context);

        prompt.ShouldContain("[Available Information]");
        prompt.ShouldContain("PreviousResult: 42");
        prompt.ShouldContain("DataFormat: JSON");
    }

    [Fact(DisplayName = "Prompt includes Task section")]
    public void BuildSolvePrompt_ShouldIncludeTaskSection()
    {
        var prompt = _solver.BuildSolvePrompt(
            "Solve this problem",
            new Dictionary<string, string>());

        prompt.ShouldContain("[Task]");
        prompt.ShouldContain("Solve this problem");
    }

    [Fact(DisplayName = "Prompt includes answer marker")]
    public void BuildSolvePrompt_ShouldIncludeAnswerMarker()
    {
        var prompt = _solver.BuildSolvePrompt(
            "Task",
            new Dictionary<string, string>());

        prompt.ShouldContain("[Your Answer]");
    }

    [Fact(DisplayName = "Prompt includes solving rules")]
    public void BuildSolvePrompt_ShouldIncludeRules()
    {
        var prompt = _solver.BuildSolvePrompt(
            "Task",
            new Dictionary<string, string>());

        prompt.ShouldContain("Rules:");
        prompt.ShouldContain("direct");
        prompt.ShouldContain("concise");
    }

    // ============================================================
    //  ExtractSolution Tests
    // ============================================================

    [Fact(DisplayName = "Plain text is trimmed and returned")]
    public void ExtractSolution_PlainText_ShouldReturnTrimmed()
    {
        var output = "  The answer is 42.  ";
        
        var result = _solver.ExtractSolution(output);

        result.ShouldBe("The answer is 42.");
    }

    [Fact(DisplayName = "Extract content from <Answer> tags")]
    public void ExtractSolution_WithAnswerTags_ShouldExtractContent()
    {
        var output = """
            Here is my response:
            <Answer>The solution is X = 5</Answer>
            That's the answer.
            """;

        var result = _solver.ExtractSolution(output);

        result.ShouldBe("The solution is X = 5");
    }

    [Fact(DisplayName = "Extract content from <Response> tags")]
    public void ExtractSolution_WithResponseTags_ShouldExtractContent()
    {
        var output = """
            <Response>
            This is the extracted response.
            </Response>
            """;

        var result = _solver.ExtractSolution(output);

        result.ShouldBe("This is the extracted response.");
    }

    [Fact(DisplayName = "Extract content from <Solution> tags")]
    public void ExtractSolution_WithSolutionTags_ShouldExtractContent()
    {
        var output = "<Solution>Final solution here</Solution>";

        var result = _solver.ExtractSolution(output);

        result.ShouldBe("Final solution here");
    }

    [Fact(DisplayName = "Remove <END> marker from output")]
    public void ExtractSolution_WithEndMarker_ShouldRemoveIt()
    {
        var output = "The answer is complete.<END>";

        var result = _solver.ExtractSolution(output);

        result.ShouldBe("The answer is complete.");
    }

    [Fact(DisplayName = "Tag matching is case insensitive")]
    public void ExtractSolution_CaseInsensitiveTags_ShouldWork()
    {
        var output = "<answer>Case insensitive</ANSWER>";

        var result = _solver.ExtractSolution(output);

        result.ShouldBe("Case insensitive");
    }

    [Fact(DisplayName = "Empty input returns empty string")]
    public void ExtractSolution_EmptyInput_ShouldReturnEmpty()
    {
        _solver.ExtractSolution("").ShouldBeEmpty();
        _solver.ExtractSolution("   ").ShouldBeEmpty();
        _solver.ExtractSolution(null!).ShouldBeEmpty();
    }

    [Fact(DisplayName = "No tags returns original trimmed content")]
    public void ExtractSolution_NoTags_ShouldReturnOriginal()
    {
        var output = "Plain text without any wrapper tags.";

        var result = _solver.ExtractSolution(output);

        result.ShouldBe("Plain text without any wrapper tags.");
    }

    [Fact(DisplayName = "Multiline content within tags is extracted")]
    public void ExtractSolution_MultilineTags_ShouldExtractContent()
    {
        var output = """
            <Answer>
            Line 1
            Line 2
            Line 3
            </Answer>
            """;

        var result = _solver.ExtractSolution(output);

        result.ShouldContain("Line 1");
        result.ShouldContain("Line 2");
        result.ShouldContain("Line 3");
    }

    [Fact(DisplayName = "Nested content is preserved")]
    public void ExtractSolution_NestedContent_ShouldPreserve()
    {
        var output = "<Answer>Result: <code>x = 5</code></Answer>";

        var result = _solver.ExtractSolution(output);

        result.ShouldBe("Result: <code>x = 5</code>");
    }

    [Fact(DisplayName = "<END> marker removal is case insensitive")]
    public void ExtractSolution_EndMarkerCaseInsensitive_ShouldRemove()
    {
        var output = "Done<end>";

        var result = _solver.ExtractSolution(output);

        result.ShouldBe("Done");
    }
}
