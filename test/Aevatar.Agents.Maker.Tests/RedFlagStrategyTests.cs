using Shouldly;
using Xunit;

namespace Aevatar.Agents.Maker.Tests;

// ============================================================
//  RedFlagStrategy Tests - Content Validation
//  Paper Reference: Section 3.3 Red-Flagging: Recognizing Signs of Unreliability
// ============================================================

public class RedFlagStrategyTests
{
    // ============================================================
    //  DefaultEnglishRedFlagStrategy Tests
    // ============================================================

    [Fact(DisplayName = "Valid content passes validation")]
    public void EnglishStrategy_ValidContent_ShouldPass()
    {
        var strategy = new DefaultEnglishRedFlagStrategy();

        var result = strategy.Validate("This is a valid response with enough content.", "P1", out var reason);

        result.ShouldBeTrue();
        reason.ShouldBeNull();
    }

    [Fact(DisplayName = "Empty content fails validation")]
    public void EnglishStrategy_EmptyContent_ShouldFail()
    {
        var strategy = new DefaultEnglishRedFlagStrategy();

        strategy.Validate("", "P1", out var reason).ShouldBeFalse();
        reason.ShouldBe("Empty content");

        strategy.Validate("   ", "P1", out reason).ShouldBeFalse();
        reason.ShouldBe("Empty content");
    }

    [Fact(DisplayName = "Content too short fails validation")]
    public void EnglishStrategy_TooShort_ShouldFail()
    {
        var strategy = new DefaultEnglishRedFlagStrategy();

        var result = strategy.Validate("Short", "P1", out var reason);

        result.ShouldBeFalse();
        reason.ShouldContain("too short");
    }

    [Fact(DisplayName = "Content too long fails validation")]
    public void EnglishStrategy_TooLong_ShouldFail()
    {
        var strategy = new DefaultEnglishRedFlagStrategy();
        var longContent = new string('x', 9000); // Default max is 8000

        var result = strategy.Validate(longContent, "P1", out var reason);

        result.ShouldBeFalse();
        reason.ShouldContain("too long");
    }

    [Theory(DisplayName = "LLM refusal prefixes are detected")]
    [InlineData("I cannot help with that request.")]
    [InlineData("I can't provide that information.")]
    [InlineData("I'm unable to assist with this.")]
    [InlineData("I am unable to complete this task.")]
    [InlineData("I apologize, but I cannot do that.")]
    [InlineData("I'm sorry, I can't help.")]
    [InlineData("As an AI, I have limitations.")]
    [InlineData("As a language model, I cannot.")]
    public void EnglishStrategy_RefusalPrefixes_ShouldFail(string refusal)
    {
        var strategy = new DefaultEnglishRedFlagStrategy();

        var result = strategy.Validate(refusal, "P1", out var reason);

        result.ShouldBeFalse();
        reason.ShouldContain("refusal");
    }

    [Fact(DisplayName = "Excessive repetition (degeneration) is detected")]
    public void EnglishStrategy_ExcessiveRepetition_ShouldFail()
    {
        var strategy = new DefaultEnglishRedFlagStrategy();
        var degenerated = "abc" + new string('x', 50) + "xyzxyzxyzxyzxyzxyzxyzxyzxyzxyzxyz"; // 11 repetitions of "xyz"

        var result = strategy.Validate(degenerated, "P1", out var reason);

        result.ShouldBeFalse();
        reason.ShouldContain("repetition");
    }

    [Fact(DisplayName = "Custom options are respected")]
    public void EnglishStrategy_CustomOptions_ShouldRespect()
    {
        var options = new RedFlagOptions
        {
            MaxContentLength = 100,
            MinContentLength = 5,
            EnableRefusalDetection = false
        };
        var strategy = new DefaultEnglishRedFlagStrategy(options);

        // Should pass refusal check now
        var result = strategy.Validate("I cannot do that but this is valid content.", "P1", out _);
        result.ShouldBeTrue();

        // Should fail length check
        var longContent = new string('x', 150);
        result = strategy.Validate(longContent, "P1", out var reason);
        result.ShouldBeFalse();
        reason.ShouldContain("too long");
    }

    [Fact(DisplayName = "Disabled validations pass all content")]
    public void EnglishStrategy_DisabledValidation_ShouldPass()
    {
        var options = new RedFlagOptions
        {
            EnableLengthValidation = false,
            EnableRefusalDetection = false,
            EnableDegenerationDetection = false
        };
        var strategy = new DefaultEnglishRedFlagStrategy(options);

        // Very short content should pass
        strategy.Validate("Hi", "P1", out _).ShouldBeTrue();

        // Refusal should pass
        strategy.Validate("I cannot help with that.", "P1", out _).ShouldBeTrue();
    }

    // ============================================================
    //  ChineseRedFlagStrategy Tests
    // ============================================================

    [Fact(DisplayName = "Chinese: Valid content passes")]
    public void ChineseStrategy_ValidContent_ShouldPass()
    {
        var strategy = new ChineseRedFlagStrategy();

        var result = strategy.Validate("这是一个有效的中文回复内容。", "P1", out var reason);

        result.ShouldBeTrue();
        reason.ShouldBeNull();
    }

    [Fact(DisplayName = "Chinese: Empty content fails")]
    public void ChineseStrategy_EmptyContent_ShouldFail()
    {
        var strategy = new ChineseRedFlagStrategy();

        strategy.Validate("", "P1", out var reason).ShouldBeFalse();
        reason.ShouldBe("内容为空");
    }

    [Theory(DisplayName = "Chinese: Refusal prefixes are detected")]
    [InlineData("我无法完成这个任务，请换一个问题。")]
    [InlineData("我不能提供这个信息，这超出了我的能力。")]
    [InlineData("抱歉，我无法帮助你完成这个请求。")]
    [InlineData("对不起，这超出了我的能力范围，无法完成。")]
    [InlineData("作为AI，我有一些限制，无法处理此请求。")]
    public void ChineseStrategy_RefusalPrefixes_ShouldFail(string refusal)
    {
        var strategy = new ChineseRedFlagStrategy();

        var result = strategy.Validate(refusal, "P1", out var reason);

        result.ShouldBeFalse();
        reason.ShouldContain("拒绝");
    }

    [Fact(DisplayName = "Chinese: Excessive repetition is detected")]
    public void ChineseStrategy_ExcessiveRepetition_ShouldFail()
    {
        var strategy = new ChineseRedFlagStrategy();
        // Need 2+ char pattern (minLen=2), repeated 10+ times
        var degenerated = "这是一段正常的前缀内容" + "哈哈哈哈哈哈哈哈哈哈哈哈哈哈哈哈哈哈哈哈哈哈";

        var result = strategy.Validate(degenerated, "P1", out var reason);

        result.ShouldBeFalse();
        reason.ShouldContain("重复");
    }

    // ============================================================
    //  NoOpRedFlagStrategy Tests
    // ============================================================

    [Fact(DisplayName = "NoOp strategy always passes")]
    public void NoOpStrategy_ShouldAlwaysPass()
    {
        var strategy = NoOpRedFlagStrategy.Instance;

        strategy.Validate("", "P1", out _).ShouldBeTrue();
        strategy.Validate("I cannot help", "P1", out _).ShouldBeTrue();
        strategy.Validate(new string('x', 100000), "P1", out _).ShouldBeTrue();
    }

    [Fact(DisplayName = "NoOp strategy is singleton")]
    public void NoOpStrategy_IsSingleton()
    {
        var instance1 = NoOpRedFlagStrategy.Instance;
        var instance2 = NoOpRedFlagStrategy.Instance;

        ReferenceEquals(instance1, instance2).ShouldBeTrue();
    }

    // ============================================================
    //  CodeAwareRedFlagStrategy Tests
    // ============================================================

    [Fact(DisplayName = "Code strategy: Long code passes")]
    public void CodeStrategy_LongCode_ShouldPass()
    {
        var strategy = new CodeAwareRedFlagStrategy();
        var longCode = new string('x', 40000); // Much longer than default

        var result = strategy.Validate(longCode, "P1", out _);

        result.ShouldBeTrue();
    }

    [Fact(DisplayName = "Code strategy: Refusal in comments passes")]
    public void CodeStrategy_RefusalInComments_ShouldPass()
    {
        var strategy = new CodeAwareRedFlagStrategy();
        var code = """
            // I cannot guarantee this works in all cases
            function calculate() {
                return 42;
            }
            """;

        var result = strategy.Validate(code, "P1", out _);

        result.ShouldBeTrue();
    }

    [Fact(DisplayName = "Code strategy: Empty content fails")]
    public void CodeStrategy_EmptyContent_ShouldFail()
    {
        var strategy = new CodeAwareRedFlagStrategy();

        var result = strategy.Validate("", "P1", out var reason);

        result.ShouldBeFalse();
        reason.ShouldBe("Empty content");
    }

    [Fact(DisplayName = "Code strategy: Very long content fails")]
    public void CodeStrategy_VeryLongContent_ShouldFail()
    {
        var strategy = new CodeAwareRedFlagStrategy();
        var tooLong = new string('x', 60000); // Default max is 50000

        var result = strategy.Validate(tooLong, "P1", out var reason);

        result.ShouldBeFalse();
        reason.ShouldContain("too long");
    }

    // ============================================================
    //  CompositeRedFlagStrategy Tests
    // ============================================================

    [Fact(DisplayName = "Composite: All strategies pass means pass")]
    public void CompositeStrategy_AllPass_ShouldPass()
    {
        var composite = new CompositeRedFlagStrategy(
            new DefaultEnglishRedFlagStrategy(),
            NoOpRedFlagStrategy.Instance);

        var result = composite.Validate("Valid content here.", "P1", out _);

        result.ShouldBeTrue();
    }

    [Fact(DisplayName = "Composite: One strategy fails means fail")]
    public void CompositeStrategy_OneFails_ShouldFail()
    {
        var composite = new CompositeRedFlagStrategy(
            new DefaultEnglishRedFlagStrategy(),
            new ChineseRedFlagStrategy());

        // English refusal - fails English strategy
        var result = composite.Validate("I cannot help with that request.", "P1", out var reason);

        result.ShouldBeFalse();
        reason.ShouldContain("refusal");
    }

    [Fact(DisplayName = "Composite: Empty strategies array passes")]
    public void CompositeStrategy_Empty_ShouldPass()
    {
        var composite = new CompositeRedFlagStrategy();

        var result = composite.Validate("Anything", "P1", out _);

        result.ShouldBeTrue();
    }

    // ============================================================
    //  RedFlagOptions Tests
    // ============================================================

    [Fact(DisplayName = "RedFlagOptions has sensible defaults")]
    public void RedFlagOptions_DefaultValues()
    {
        var options = new RedFlagOptions();

        options.MaxContentLength.ShouldBe(8000);
        options.MinContentLength.ShouldBe(10);
        options.DegenerationMinLength.ShouldBe(3);
        options.DegenerationMaxRepetitions.ShouldBe(10);
        options.EnableRefusalDetection.ShouldBeTrue();
        options.EnableDegenerationDetection.ShouldBeTrue();
        options.EnableLengthValidation.ShouldBeTrue();
        options.CustomRefusalPrefixes.ShouldBeNull();
    }

    [Fact(DisplayName = "Custom refusal prefixes are used")]
    public void RedFlagOptions_CustomRefusalPrefixes_ShouldBeUsed()
    {
        var options = new RedFlagOptions
        {
            CustomRefusalPrefixes = new[] { "NOPE:", "DENIED:" }
        };
        var strategy = new DefaultEnglishRedFlagStrategy(options);

        strategy.Validate("NOPE: I won't do that", "P1", out var reason).ShouldBeFalse();
        reason.ShouldContain("refusal");

        // Standard refusals should not trigger
        strategy.Validate("I cannot help but this is different", "P1", out _).ShouldBeTrue();
    }
}
