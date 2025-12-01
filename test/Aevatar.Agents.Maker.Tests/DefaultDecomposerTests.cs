using Shouldly;
using Xunit;

namespace Aevatar.Agents.Maker.Tests;

// ============================================================
//  DefaultDecomposer Tests - Task Breakdown Logic
//  Paper Reference: Section 3.1 Maximal Agentic Decomposition
// ============================================================

public class DefaultDecomposerTests
{
    private readonly DefaultDecomposer _decomposer = new();

    // ============================================================
    //  BuildDecompositionPrompt Tests
    // ============================================================

    [Fact(DisplayName = "Prompt excludes Context section when context is empty")]
    public void BuildDecompositionPrompt_WithEmptyContext_ShouldNotIncludeContextSection()
    {
        var prompt = _decomposer.BuildDecompositionPrompt(
            "Analyze the data",
            new Dictionary<string, string>());

        prompt.ShouldContain("Analyze the data");
        prompt.ShouldNotContain("[Context]");
    }

    [Fact(DisplayName = "Prompt includes Context section when context provided")]
    public void BuildDecompositionPrompt_WithContext_ShouldIncludeContextSection()
    {
        var context = new Dictionary<string, string>
        {
            ["DataSource"] = "SQL Database",
            ["Format"] = "JSON"
        };

        var prompt = _decomposer.BuildDecompositionPrompt("Analyze the data", context);

        prompt.ShouldContain("[Context]");
        prompt.ShouldContain("DataSource: SQL Database");
        prompt.ShouldContain("Format: JSON");
    }

    [Theory(DisplayName = "Different granularity levels generate appropriate instructions")]
    [InlineData(DecompositionGranularity.Balanced, "3-6")]
    [InlineData(DecompositionGranularity.Binary, "EXACTLY 2")]
    [InlineData(DecompositionGranularity.Single, "SINGLE NEXT STEP")]
    public void BuildDecompositionPrompt_WithGranularity_ShouldIncludeAppropriateInstruction(
        DecompositionGranularity granularity, string expectedPhrase)
    {
        var prompt = _decomposer.BuildDecompositionPrompt(
            "Task",
            new Dictionary<string, string>(),
            granularity);

        prompt.ShouldContain(expectedPhrase);
    }

    [Fact(DisplayName = "Prompt requests JSON array output format")]
    public void BuildDecompositionPrompt_ShouldRequestJsonFormat()
    {
        var prompt = _decomposer.BuildDecompositionPrompt(
            "Task",
            new Dictionary<string, string>());

        prompt.ShouldContain("JSON array");
        prompt.ShouldContain("step_id");
        prompt.ShouldContain("description");
    }

    // ============================================================
    //  IsAtomic Tests - Heuristic Atomicity Detection
    // ============================================================

    [Fact(DisplayName = "Short description (<100 chars) is atomic")]
    public void IsAtomic_ShortDescription_ShouldReturnTrue()
    {
        // Less than 100 characters
        var result = _decomposer.IsAtomic("Calculate the sum of two numbers", 0);
        result.ShouldBeTrue();
    }

    [Fact(DisplayName = "Long description (>100 chars) is non-atomic")]
    public void IsAtomic_LongDescription_ShouldReturnFalse()
    {
        // More than 100 characters without atomic keywords
        var longTask = new string('x', 150);
        var result = _decomposer.IsAtomic(longTask, 0);
        result.ShouldBeFalse();
    }

    [Theory(DisplayName = "Atomic keywords force atomic classification")]
    [InlineData("Perform a single calculation on the dataset")]
    [InlineData("Execute one step of the algorithm")]
    [InlineData("This is an atomic operation that cannot be split")]
    public void IsAtomic_WithAtomicKeywords_ShouldReturnTrue(string description)
    {
        // Pad to > 100 chars but contains atomic keywords
        var padded = description + new string(' ', 100);
        var result = _decomposer.IsAtomic(padded, 0);
        result.ShouldBeTrue();
    }

    [Fact(DisplayName = "Depth >= 10 forces atomic classification")]
    public void IsAtomic_AtDepth10OrMore_ShouldReturnTrue()
    {
        var longTask = new string('x', 150);
        
        _decomposer.IsAtomic(longTask, 9).ShouldBeFalse();
        _decomposer.IsAtomic(longTask, 10).ShouldBeTrue();
        _decomposer.IsAtomic(longTask, 15).ShouldBeTrue();
    }

    // ============================================================
    //  ParseDecomposition Tests - JSON Parsing
    // ============================================================

    [Fact(DisplayName = "Parse standard JSON format decomposition")]
    public void ParseDecomposition_ValidJson_ShouldReturnSteps()
    {
        var json = """
            [
                {"step_id": "S1", "description": "Gather requirements"},
                {"step_id": "S2", "description": "Design solution"},
                {"step_id": "S3", "description": "Implement code"}
            ]
            """;

        var steps = _decomposer.ParseDecomposition(json);

        steps.Count.ShouldBe(3);
        steps[0].StepId.ShouldBe("S1");
        steps[0].Description.ShouldBe("Gather requirements");
        steps[1].StepId.ShouldBe("S2");
        steps[2].StepId.ShouldBe("S3");
    }

    [Fact(DisplayName = "Extract JSON array from mixed text response")]
    public void ParseDecomposition_WithExtraText_ShouldExtractJsonArray()
    {
        var response = """
            Here are the steps:
            [
                {"step_id": "S1", "description": "First step"},
                {"step_id": "S2", "description": "Second step"}
            ]
            These are the decomposed tasks.
            """;

        var steps = _decomposer.ParseDecomposition(response);

        steps.Count.ShouldBe(2);
    }

    [Fact(DisplayName = "Auto-generate step_id when missing")]
    public void ParseDecomposition_MissingStepId_ShouldAutoGenerate()
    {
        var json = """
            [
                {"description": "First task"},
                {"description": "Second task"}
            ]
            """;

        var steps = _decomposer.ParseDecomposition(json);

        steps.Count.ShouldBe(2);
        steps[0].StepId.ShouldBe("S01");
        steps[1].StepId.ShouldBe("S02");
    }

    [Fact(DisplayName = "Support alternative field names: task, section")]
    public void ParseDecomposition_AlternativeFieldNames_ShouldParse()
    {
        // LLMs might use "task" or "section" instead of "description"
        var json = """
            [
                {"step_id": "S1", "task": "Do something"},
                {"step_id": "S2", "section": "Another thing"}
            ]
            """;

        var steps = _decomposer.ParseDecomposition(json);

        steps.Count.ShouldBe(2);
        steps[0].Description.ShouldBe("Do something");
        steps[1].Description.ShouldBe("Another thing");
    }

    [Fact(DisplayName = "Support pure string array format")]
    public void ParseDecomposition_StringArray_ShouldParse()
    {
        var json = """["First step", "Second step", "Third step"]""";

        var steps = _decomposer.ParseDecomposition(json);

        steps.Count.ShouldBe(3);
        steps[0].Description.ShouldBe("First step");
        steps[1].Description.ShouldBe("Second step");
    }

    [Fact(DisplayName = "Empty input returns empty list")]
    public void ParseDecomposition_EmptyInput_ShouldReturnEmptyList()
    {
        _decomposer.ParseDecomposition("").ShouldBeEmpty();
        _decomposer.ParseDecomposition("   ").ShouldBeEmpty();
        _decomposer.ParseDecomposition(null!).ShouldBeEmpty();
    }

    [Fact(DisplayName = "Invalid JSON falls back to line parsing")]
    public void ParseDecomposition_InvalidJson_ShouldFallbackToLineParsing()
    {
        var text = """
            - First step to complete
            - Second step to do
            - Third step
            """;

        var steps = _decomposer.ParseDecomposition(text);

        steps.Count.ShouldBe(3);
        steps[0].Description.ShouldBe("First step to complete");
    }

    [Fact(DisplayName = "Support numbered list format")]
    public void ParseDecomposition_NumberedList_ShouldParse()
    {
        var text = """
            1. Analyze requirements
            2. Design architecture
            3. Write code
            """;

        var steps = _decomposer.ParseDecomposition(text);

        steps.Count.ShouldBe(3);
        steps[0].Description.ShouldBe("Analyze requirements");
    }

    [Fact(DisplayName = "Tolerate trailing commas in JSON")]
    public void ParseDecomposition_WithTrailingCommas_ShouldParse()
    {
        var json = """
            [
                {"step_id": "S1", "description": "First"},
                {"step_id": "S2", "description": "Second"},
            ]
            """;

        var steps = _decomposer.ParseDecomposition(json);

        steps.Count.ShouldBe(2);
    }

    [Fact(DisplayName = "Filter out steps with empty descriptions")]
    public void ParseDecomposition_EmptyDescriptions_ShouldBeFiltered()
    {
        var json = """
            [
                {"step_id": "S1", "description": "Valid step"},
                {"step_id": "S2", "description": ""},
                {"step_id": "S3", "description": "   "},
                {"step_id": "S4", "description": "Another valid"}
            ]
            """;

        var steps = _decomposer.ParseDecomposition(json);

        steps.Count.ShouldBe(2);
        steps[0].Description.ShouldBe("Valid step");
        steps[1].Description.ShouldBe("Another valid");
    }

    [Fact(DisplayName = "Fallback to any string property as description")]
    public void ParseDecomposition_FallbackToAnyStringProperty()
    {
        // When no known field names match, use any string property
        var json = """
            [
                {"step_id": "S1", "content": "First task"},
                {"step_id": "S2", "action": "Second task"}
            ]
            """;

        var steps = _decomposer.ParseDecomposition(json);

        steps.Count.ShouldBe(2);
        steps[0].Description.ShouldBe("First task");
        steps[1].Description.ShouldBe("Second task");
    }
}
