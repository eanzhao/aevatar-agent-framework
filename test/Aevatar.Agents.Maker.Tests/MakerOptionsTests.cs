using Shouldly;
using Xunit;

namespace Aevatar.Agents.Maker.Tests;

// ============================================================
//  MakerOptions Tests - Configuration Validation
//  Paper Reference: MAKER system parameters and scaling laws
// ============================================================

public class MakerOptionsTests
{
    // ============================================================
    //  Default Values Tests
    // ============================================================

    [Fact(DisplayName = "Default options have sensible values")]
    public void DefaultOptions_ShouldHaveReasonableDefaults()
    {
        var options = new MakerOptions();

        options.Reliability.ShouldBe(ReliabilityLevel.Medium);
        options.CustomK.ShouldBeNull();
        options.MaxTotalLlmCalls.ShouldBe(500);
        options.MaxTotalTokens.ShouldBe(2_000_000);
        options.MaxDuration.ShouldBe(TimeSpan.FromMinutes(30));
        options.DepthWarningThreshold.ShouldBe(10);
        options.StepTimeout.ShouldBe(TimeSpan.FromSeconds(60));
        options.Mode.ShouldBe(ExecutionMode.Production);
        options.ContextIsolation.ShouldBe(ContextIsolationMode.Full);
        options.Granularity.ShouldBe(DecompositionGranularity.Balanced);
        options.HardDepthCap.ShouldBe(50);
    }

    [Fact(DisplayName = "Default strategies are null (use defaults)")]
    public void DefaultOptions_StrategiesShouldBeNull()
    {
        var options = new MakerOptions();

        options.Decomposer.ShouldBeNull();
        options.Solver.ShouldBeNull();
        options.Composer.ShouldBeNull();
        options.RedFlagStrategy.ShouldBeNull();
    }

    [Fact(DisplayName = "Resilience features enabled by default")]
    public void DefaultOptions_ResilienceEnabled()
    {
        var options = new MakerOptions();

        options.EnableResilience.ShouldBeTrue();
        options.MaxRetries.ShouldBe(3);
        options.InitialRetryDelay.ShouldBe(TimeSpan.FromSeconds(1));
        options.MaxRetryDelay.ShouldBe(TimeSpan.FromSeconds(30));
        options.EnableCircuitBreaker.ShouldBeTrue();
        options.CircuitBreakerThreshold.ShouldBe(5);
        options.CircuitBreakerDuration.ShouldBe(TimeSpan.FromMinutes(1));
        options.EnableCheckpointing.ShouldBeTrue();
        options.CheckpointDirectory.ShouldBeNull();
    }

    // ============================================================
    //  ConsensusK Computed Property Tests
    // ============================================================

    [Theory(DisplayName = "ConsensusK matches ReliabilityLevel")]
    [InlineData(ReliabilityLevel.Low, 1)]
    [InlineData(ReliabilityLevel.Medium, 2)]
    [InlineData(ReliabilityLevel.High, 3)]
    [InlineData(ReliabilityLevel.VeryHigh, 4)]
    [InlineData(ReliabilityLevel.Critical, 5)]
    [InlineData(ReliabilityLevel.UltraCritical, 7)]
    [InlineData(ReliabilityLevel.Extreme, 10)]
    public void ConsensusK_ShouldMatchReliabilityLevel(ReliabilityLevel level, int expectedK)
    {
        var options = new MakerOptions { Reliability = level };

        options.ConsensusK.ShouldBe(expectedK);
    }

    [Fact(DisplayName = "CustomK overrides ReliabilityLevel")]
    public void ConsensusK_WithCustomK_ShouldOverrideReliability()
    {
        var options = new MakerOptions
        {
            Reliability = ReliabilityLevel.Low, // K=1
            CustomK = 5 // Override to 5
        };

        options.ConsensusK.ShouldBe(5);
    }

    // ============================================================
    //  SamplesPerRound Computed Property Tests
    // ============================================================

    [Theory(DisplayName = "SamplesPerRound = 2K - 1 (paper formula)")]
    [InlineData(ReliabilityLevel.Low, 1)]      // 2*1-1 = 1
    [InlineData(ReliabilityLevel.Medium, 3)]   // 2*2-1 = 3
    [InlineData(ReliabilityLevel.High, 5)]     // 2*3-1 = 5
    [InlineData(ReliabilityLevel.VeryHigh, 7)] // 2*4-1 = 7
    [InlineData(ReliabilityLevel.Critical, 9)] // 2*5-1 = 9
    public void SamplesPerRound_ShouldBe_2K_Minus_1(ReliabilityLevel level, int expectedN)
    {
        var options = new MakerOptions { Reliability = level };

        options.SamplesPerRound.ShouldBe(expectedN);
    }

    [Fact(DisplayName = "SamplesPerRound with CustomK")]
    public void SamplesPerRound_WithCustomK_ShouldCalculateCorrectly()
    {
        var options = new MakerOptions { CustomK = 4 };

        options.SamplesPerRound.ShouldBe(7); // 2*4-1 = 7
    }

    // ============================================================
    //  ExecutionMode Tests
    // ============================================================

    [Fact(DisplayName = "Production mode is default")]
    public void ExecutionMode_Production_IsDefault()
    {
        var options = new MakerOptions();

        options.Mode.ShouldBe(ExecutionMode.Production);
    }

    [Fact(DisplayName = "Academic mode can be set")]
    public void ExecutionMode_Academic_CanBeSet()
    {
        var options = new MakerOptions { Mode = ExecutionMode.Academic };

        options.Mode.ShouldBe(ExecutionMode.Academic);
    }

    // ============================================================
    //  ContextIsolationMode Tests
    // ============================================================

    [Fact(DisplayName = "Full context isolation is default")]
    public void ContextIsolation_Full_IsDefault()
    {
        var options = new MakerOptions();

        options.ContextIsolation.ShouldBe(ContextIsolationMode.Full);
    }

    [Theory(DisplayName = "All context isolation modes are valid")]
    [InlineData(ContextIsolationMode.Full)]
    [InlineData(ContextIsolationMode.Minimal)]
    [InlineData(ContextIsolationMode.None)]
    public void ContextIsolation_AllModesValid(ContextIsolationMode mode)
    {
        var options = new MakerOptions { ContextIsolation = mode };

        options.ContextIsolation.ShouldBe(mode);
    }

    // ============================================================
    //  DecompositionGranularity Tests
    // ============================================================

    [Fact(DisplayName = "Balanced granularity is default")]
    public void Granularity_Balanced_IsDefault()
    {
        var options = new MakerOptions();

        options.Granularity.ShouldBe(DecompositionGranularity.Balanced);
    }

    [Theory(DisplayName = "All granularity modes are valid")]
    [InlineData(DecompositionGranularity.Balanced)]
    [InlineData(DecompositionGranularity.Binary)]
    [InlineData(DecompositionGranularity.Single)]
    public void Granularity_AllModesValid(DecompositionGranularity granularity)
    {
        var options = new MakerOptions { Granularity = granularity };

        options.Granularity.ShouldBe(granularity);
    }

    // ============================================================
    //  Advanced Parameters Tests
    // ============================================================

    [Fact(DisplayName = "ClusteringMethod default is auto")]
    public void ClusteringMethod_DefaultIsAuto()
    {
        var options = new MakerOptions();

        options.ClusteringMethod.ShouldBe("auto");
    }

    [Fact(DisplayName = "SemanticSimilarityThreshold default is 0.85")]
    public void SemanticSimilarityThreshold_DefaultIs085()
    {
        var options = new MakerOptions();

        options.SemanticSimilarityThreshold.ShouldBe(0.85f);
    }

    [Fact(DisplayName = "Temperature settings have reasonable defaults")]
    public void TemperatureSettings_HaveReasonableDefaults()
    {
        var options = new MakerOptions();

        options.BaseTemperature.ShouldBe(0.3f);
        options.TemperatureVariance.ShouldBe(0.1f);
    }

    [Fact(DisplayName = "UseMultipleProviders default is false")]
    public void UseMultipleProviders_DefaultIsFalse()
    {
        var options = new MakerOptions();

        options.UseMultipleProviders.ShouldBeFalse();
        options.CoordinatorProviderName.ShouldBeNull();
    }

    [Fact(DisplayName = "Token limits have reasonable defaults")]
    public void TokenLimits_HaveReasonableDefaults()
    {
        var options = new MakerOptions();

        options.MaxDecompositionTokens.ShouldBe(1024 * 10);
        options.MaxSolutionTokens.ShouldBe(1024 * 20);
    }

    // ============================================================
    //  Record Immutability Tests
    // ============================================================

    [Fact(DisplayName = "Options is immutable record")]
    public void Options_ShouldBeImmutableRecord()
    {
        var options1 = new MakerOptions { Reliability = ReliabilityLevel.High };
        var options2 = options1 with { MaxRetries = 5 };

        options1.MaxRetries.ShouldBe(3); // Original unchanged
        options2.MaxRetries.ShouldBe(5); // New copy modified
        options2.Reliability.ShouldBe(ReliabilityLevel.High); // Inherited from original
    }

    [Fact(DisplayName = "With expression creates new instance")]
    public void Options_WithExpression_ShouldCreateNewInstance()
    {
        var original = new MakerOptions();
        var modified = original with { Mode = ExecutionMode.Academic };

        ReferenceEquals(original, modified).ShouldBeFalse();
        original.Mode.ShouldBe(ExecutionMode.Production);
        modified.Mode.ShouldBe(ExecutionMode.Academic);
    }

    // ============================================================
    //  Context Dictionary Tests
    // ============================================================

    [Fact(DisplayName = "Context default is null")]
    public void Context_DefaultIsNull()
    {
        var options = new MakerOptions();

        options.Context.ShouldBeNull();
    }

    [Fact(DisplayName = "Context can be set")]
    public void Context_CanBeSet()
    {
        var context = new Dictionary<string, string>
        {
            ["key1"] = "value1",
            ["key2"] = "value2"
        };

        var options = new MakerOptions { Context = context };

        options.Context.ShouldNotBeNull();
        options.Context.Count.ShouldBe(2);
        options.Context["key1"].ShouldBe("value1");
    }

    // ============================================================
    //  Callback Tests
    // ============================================================

    [Fact(DisplayName = "OnProgress default is null")]
    public void OnProgress_DefaultIsNull()
    {
        var options = new MakerOptions();

        options.OnProgress.ShouldBeNull();
    }

    [Fact(DisplayName = "OnProgress can be set and invoked")]
    public void OnProgress_CanBeSet()
    {
        var callCount = 0;
        var options = new MakerOptions
        {
            OnProgress = _ => callCount++
        };

        options.OnProgress.ShouldNotBeNull();
        options.OnProgress!(new MakerProgress
        {
            Phase = MakerPhase.Starting,
            TaskId = "T1",
            Message = "Test"
        });
        callCount.ShouldBe(1);
    }

    // ============================================================
    //  RedFlagOptions Tests
    // ============================================================

    [Fact(DisplayName = "RedFlagOptions has default instance")]
    public void RedFlagOptions_HasDefaultInstance()
    {
        var options = new MakerOptions();

        options.RedFlagOptions.ShouldNotBeNull();
        options.RedFlagOptions.MaxContentLength.ShouldBe(8000);
    }

    [Fact(DisplayName = "RedFlagThreshold default is 3")]
    public void RedFlagThreshold_DefaultIs3()
    {
        var options = new MakerOptions();

        options.RedFlagThreshold.ShouldBe(3);
    }
}
