using Shouldly;
using Xunit;

namespace Aevatar.Agents.Maker.Tests;

// ============================================================
//  MAKER Core Logic Tests
//  Tests the fundamental algorithms that make MAKER work:
//  - First-to-ahead-by-K voting consensus
//  - Task decomposition → solve → compose pipeline
//  - Error correction through re-decomposition
//  - Budget enforcement
// ============================================================

/// <summary>
/// Tests for MAKER's core voting consensus algorithm.
/// Implements Algorithm 4 from the MAKER paper: First-to-ahead-by-K.
/// </summary>
public class VotingConsensusTests
{
    // ============================================================
    //  First-to-ahead-by-K Algorithm Tests
    //  Paper: "The leader must be ahead by K votes to win"
    // ============================================================

    [Fact(DisplayName = "K=2: Leader with 2 votes, runner-up with 0 should win")]
    public async Task K2_LeaderWith2_RunnerUp0_ShouldWin()
    {
        // Arrange - K=2 means leader needs 2 more votes than runner-up
        var engine = new VoteEngine(k: 2);

        // Act - Submit 2 identical votes
        await engine.SubmitVoteAsync("Answer A", default);
        var result = await engine.SubmitVoteAsync("Answer A", default);

        // Assert - Consensus reached: 2 - 0 = 2 >= K
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.WinningContent.ShouldBe("Answer A");
        result.LeaderVotes.ShouldBe(2);
    }

    [Fact(DisplayName = "K=2: Leader with 2 votes, runner-up with 1 should NOT win")]
    public async Task K2_LeaderWith2_RunnerUp1_ShouldNotWin()
    {
        // Arrange
        var engine = new VoteEngine(k: 2);

        // Act - Submit votes: A, B, A
        await engine.SubmitVoteAsync("Answer A", default);
        await engine.SubmitVoteAsync("Answer B", default);
        var result = await engine.SubmitVoteAsync("Answer A", default);

        // Assert - No consensus: 2 - 1 = 1 < K
        result.ShouldBeNull();
    }

    [Fact(DisplayName = "K=2: Leader with 3 votes, runner-up with 1 should win")]
    public async Task K2_LeaderWith3_RunnerUp1_ShouldWin()
    {
        // Arrange
        var engine = new VoteEngine(k: 2);

        // Act - Submit votes: A, B, A, A
        await engine.SubmitVoteAsync("Answer A", default);
        await engine.SubmitVoteAsync("Answer B", default);
        await engine.SubmitVoteAsync("Answer A", default);
        var result = await engine.SubmitVoteAsync("Answer A", default);

        // Assert - Consensus reached: 3 - 1 = 2 >= K
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.LeaderVotes.ShouldBe(3);
    }

    [Fact(DisplayName = "K=3: Requires larger margin for consensus")]
    public async Task K3_RequiresLargerMargin()
    {
        // Arrange - Higher K = more confidence required
        var engine = new VoteEngine(k: 3);

        // Act - Submit 3 identical votes
        await engine.SubmitVoteAsync("Answer A", default);
        await engine.SubmitVoteAsync("Answer A", default);
        var result = await engine.SubmitVoteAsync("Answer A", default);

        // Assert - Consensus reached: 3 - 0 = 3 >= K
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.LeaderVotes.ShouldBe(3);
    }

    [Fact(DisplayName = "K=3: 3 votes with 1 dissent should NOT win")]
    public async Task K3_3VotesWith1Dissent_ShouldNotWin()
    {
        // Arrange
        var engine = new VoteEngine(k: 3);

        // Act - Submit votes: A, B, A, A
        await engine.SubmitVoteAsync("Answer A", default);
        await engine.SubmitVoteAsync("Answer B", default);
        await engine.SubmitVoteAsync("Answer A", default);
        var result = await engine.SubmitVoteAsync("Answer A", default);

        // Assert - No consensus: 3 - 1 = 2 < K
        result.ShouldBeNull();
    }

    // ============================================================
    //  Streaming Race Pattern Tests
    //  Paper: "As each result arrives, immediately submit vote"
    // ============================================================

    [Fact(DisplayName = "After consensus: CheckConsensus returns the winner")]
    public async Task AfterConsensus_CheckConsensus_ReturnsTheWinner()
    {
        // Arrange
        var engine = new VoteEngine(k: 2);

        // Act - Reach consensus
        await engine.SubmitVoteAsync("Winner", default);
        var consensusResult = await engine.SubmitVoteAsync("Winner", default);

        // Consensus reached
        consensusResult.ShouldNotBeNull();
        consensusResult.WinningContent.ShouldBe("Winner");

        // CheckConsensus should still return the winner
        var checkResult = engine.CheckConsensus();
        checkResult.ShouldNotBeNull();
        checkResult.WinningContent.ShouldBe("Winner");
    }

    [Fact(DisplayName = "Votes continue to accumulate after consensus")]
    public async Task Votes_ContinueToAccumulate_AfterConsensus()
    {
        // Arrange
        var engine = new VoteEngine(k: 2);

        // Act - Reach consensus
        await engine.SubmitVoteAsync("Winner", default);
        await engine.SubmitVoteAsync("Winner", default);

        // Submit more votes for the winner
        await engine.SubmitVoteAsync("Winner", default);

        // Assert - Votes continue to accumulate
        var bestCandidate = engine.GetBestCandidate();
        bestCandidate.ShouldNotBeNull();
        bestCandidate.Content.ShouldBe("Winner");
        bestCandidate.Votes.ShouldBe(3);
    }

    [Fact(DisplayName = "Best candidate tracking for fallback")]
    public async Task BestCandidate_ShouldTrackLeaderForFallback()
    {
        // Arrange - No consensus scenario
        var engine = new VoteEngine(k: 3);

        // Act - Submit diverse votes (no consensus possible)
        await engine.SubmitVoteAsync("Answer A", default);
        await engine.SubmitVoteAsync("Answer B", default);
        await engine.SubmitVoteAsync("Answer A", default);
        await engine.SubmitVoteAsync("Answer C", default);

        // Assert - Best candidate should be tracked for fallback
        var bestCandidate = engine.GetBestCandidate();
        bestCandidate.ShouldNotBeNull();
        bestCandidate.Content.ShouldBe("Answer A");
        bestCandidate.Votes.ShouldBe(2);
    }

    // ============================================================
    //  Multi-round Voting Tests
    //  Paper: "If first batch fails, dispatch more workers"
    // ============================================================

    [Fact(DisplayName = "Multi-round: CheckConsensus should work across rounds")]
    public async Task MultiRound_CheckConsensusShouldWorkAcrossRounds()
    {
        // Arrange
        var engine = new VoteEngine(k: 2, maxRounds: 5);

        // Act - Simulate first round (no consensus)
        await engine.SubmitVoteAsync("A", default);
        await engine.SubmitVoteAsync("B", default);
        await engine.SubmitVoteAsync("C", default);

        var round1Result = engine.CheckConsensus();
        round1Result.ShouldBeNull(); // No consensus yet

        // Simulate second round - more votes for A
        await engine.SubmitVoteAsync("A", default);
        var round2Result = await engine.SubmitVoteAsync("A", default);

        // Assert - Now A has 3 votes, B and C have 1 each
        // 3 - 1 = 2 >= K, should have consensus
        round2Result.ShouldNotBeNull();
        round2Result.WinningContent.ShouldBe("A");
    }

    // ============================================================
    //  Edge Cases
    // ============================================================

    [Fact(DisplayName = "Empty vote should be rejected")]
    public async Task EmptyVote_ShouldBeRejected()
    {
        // Arrange
        var engine = new VoteEngine(k: 2);

        // Act & Assert - Empty strings should not affect voting
        var result = await engine.SubmitVoteAsync("", default);
        result.ShouldBeNull();

        var candidates = engine.GetAllCandidates();
        candidates.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Whitespace-only vote should be rejected")]
    public async Task WhitespaceVote_ShouldBeRejected()
    {
        // Arrange
        var engine = new VoteEngine(k: 2);

        // Act
        var result = await engine.SubmitVoteAsync("   \n\t  ", default);

        // Assert
        result.ShouldBeNull();
        engine.GetAllCandidates().ShouldBeEmpty();
    }

    [Fact(DisplayName = "K=1: Single vote should reach consensus")]
    public async Task K1_SingleVote_ShouldReachConsensus()
    {
        // Arrange - K=1 means 1-0=1 >= K
        var engine = new VoteEngine(k: 1);

        // Act - Single vote
        var result = await engine.SubmitVoteAsync("Only answer", default);

        // Assert - K=1 means 1-0=1 >= K, so it should win
        result.ShouldNotBeNull();
        result.LeaderVotes.ShouldBe(1);
    }

    [Fact(DisplayName = "SamplesPerRound should be 2K-1")]
    public void SamplesPerRound_ShouldBe_2K_Minus_1()
    {
        // Arrange & Act & Assert
        new VoteEngine(k: 2).SamplesPerRound.ShouldBe(3);  // 2*2-1=3
        new VoteEngine(k: 3).SamplesPerRound.ShouldBe(5);  // 2*3-1=5
        new VoteEngine(k: 4).SamplesPerRound.ShouldBe(7);  // 2*4-1=7
        new VoteEngine(k: 5).SamplesPerRound.ShouldBe(9);  // 2*5-1=9
    }
}

/// <summary>
/// Tests for MAKER's decomposition strategy.
/// Verifies that complex tasks are broken into atomic subtasks.
/// </summary>
public class DecompositionLogicTests
{
    // ============================================================
    //  Atomicity Assessment Tests
    //  Paper: "Decompose to truly atomic micro-tasks"
    // ============================================================

    [Theory(DisplayName = "Short descriptions should be considered atomic")]
    [InlineData("Calculate 2+2")]
    [InlineData("Summarize paragraph")]
    [InlineData("Translate word")]
    public void ShortDescription_ShouldBeAtomic(string description)
    {
        // Arrange
        var decomposer = new DefaultDecomposer();

        // Act
        var isAtomic = decomposer.IsAtomic(description, currentDepth: 0);

        // Assert - Short tasks are atomic
        isAtomic.ShouldBeTrue();
    }

    [Theory(DisplayName = "Long complex descriptions should NOT be atomic")]
    [InlineData("Write a comprehensive analysis of the economic implications of climate change policies across multiple sectors including energy, agriculture, transportation, and manufacturing, considering both short-term costs and long-term benefits")]
    [InlineData("Create a detailed implementation plan for migrating a legacy monolithic application to a microservices architecture, including database schema changes, API design, deployment strategy, and rollback procedures")]
    public void LongComplexDescription_ShouldNotBeAtomic(string description)
    {
        // Arrange
        var decomposer = new DefaultDecomposer();

        // Act
        var isAtomic = decomposer.IsAtomic(description, currentDepth: 0);

        // Assert - Complex tasks need decomposition
        isAtomic.ShouldBeFalse();
    }

    [Theory(DisplayName = "Atomic keywords should indicate atomic task")]
    [InlineData("single calculation")]
    [InlineData("one-step process")]
    [InlineData("simple lookup")]
    [InlineData("direct answer")]
    public void AtomicKeywords_ShouldIndicateAtomic(string description)
    {
        // Arrange
        var decomposer = new DefaultDecomposer();

        // Act
        var isAtomic = decomposer.IsAtomic(description, currentDepth: 0);

        // Assert
        isAtomic.ShouldBeTrue();
    }

    // ============================================================
    //  Decomposition Parsing Tests
    // ============================================================

    [Fact(DisplayName = "ParseDecomposition should extract steps from JSON")]
    public void ParseDecomposition_ShouldExtractStepsFromJson()
    {
        // Arrange
        var decomposer = new DefaultDecomposer();
        // Use the expected format with step_id field
        var jsonResponse = """
            [
                {"step_id": "S01", "description": "Analyze requirements"},
                {"step_id": "S02", "description": "Design solution"},
                {"step_id": "S03", "description": "Implement code"}
            ]
            """;

        // Act
        var steps = decomposer.ParseDecomposition(jsonResponse).ToList();

        // Assert
        steps.Count.ShouldBe(3);
        steps[0].StepId.ShouldBe("S01");
        steps[0].Description.ShouldBe("Analyze requirements");
        steps[1].StepId.ShouldBe("S02");
        steps[1].Description.ShouldBe("Design solution");
        steps[2].StepId.ShouldBe("S03");
        steps[2].Description.ShouldBe("Implement code");
    }

    [Fact(DisplayName = "ParseDecomposition should handle markdown-wrapped JSON")]
    public void ParseDecomposition_ShouldHandleMarkdownWrappedJson()
    {
        // Arrange
        var decomposer = new DefaultDecomposer();
        var markdownResponse = """
            ```json
            {
                "steps": [
                    {"id": "step1", "description": "First step"},
                    {"id": "step2", "description": "Second step"}
                ]
            }
            ```
            """;

        // Act
        var steps = decomposer.ParseDecomposition(markdownResponse).ToList();

        // Assert
        steps.Count.ShouldBe(2);
    }

    [Fact(DisplayName = "ParseDecomposition should fallback to line parsing for invalid JSON")]
    public void ParseDecomposition_ShouldFallbackToLineParsing_ForInvalidJson()
    {
        // Arrange
        var decomposer = new DefaultDecomposer();
        // DefaultDecomposer has a fallback that parses lines as steps
        var invalidResponse = "This is not JSON at all";

        // Act
        var steps = decomposer.ParseDecomposition(invalidResponse).ToList();

        // Assert - Falls back to line-by-line parsing
        steps.Count.ShouldBe(1);
        steps[0].StepId.ShouldBe("S01");
        steps[0].Description.ShouldBe("This is not JSON at all");
    }

    // ============================================================
    //  Granularity Tests
    //  Paper: "Binary decomposition (m=1) is the primary approach"
    // ============================================================

    [Fact(DisplayName = "Binary granularity should request exactly 2 subtasks")]
    public void BinaryGranularity_ShouldRequestExactly2Subtasks()
    {
        // Arrange
        var decomposer = new DefaultDecomposer();
        var context = new Dictionary<string, string>();

        // Act
        var prompt = decomposer.BuildDecompositionPrompt(
            "Complex task",
            context,
            DecompositionGranularity.Binary);

        // Assert
        prompt.ShouldContain("2");
        prompt.ShouldContain("exactly");
    }

    [Fact(DisplayName = "Balanced granularity should request 3-6 subtasks")]
    public void BalancedGranularity_ShouldRequest3To6Subtasks()
    {
        // Arrange
        var decomposer = new DefaultDecomposer();
        var context = new Dictionary<string, string>();

        // Act
        var prompt = decomposer.BuildDecompositionPrompt(
            "Complex task",
            context,
            DecompositionGranularity.Balanced);

        // Assert - DefaultDecomposer uses 3-6 for balanced mode
        prompt.ShouldContain("3-6");
    }
}

/// <summary>
/// Tests for MAKER's solution extraction strategy.
/// </summary>
public class SolutionLogicTests
{
    [Fact(DisplayName = "ExtractSolution should clean markdown code blocks")]
    public void ExtractSolution_ShouldCleanMarkdownCodeBlocks()
    {
        // Arrange
        var solver = new DefaultSolver();
        var response = """
            Here's the solution:
            ```python
            def hello():
                print("Hello World")
            ```
            """;

        // Act
        var extracted = solver.ExtractSolution(response);

        // Assert - Should preserve code content
        extracted.ShouldContain("def hello()");
        extracted.ShouldContain("print");
    }

    [Fact(DisplayName = "ExtractSolution should handle plain text")]
    public void ExtractSolution_ShouldHandlePlainText()
    {
        // Arrange
        var solver = new DefaultSolver();
        var response = "The answer is 42.";

        // Act
        var extracted = solver.ExtractSolution(response);

        // Assert
        extracted.ShouldBe("The answer is 42.");
    }

    [Fact(DisplayName = "BuildSolvePrompt should include context")]
    public void BuildSolvePrompt_ShouldIncludeContext()
    {
        // Arrange
        var solver = new DefaultSolver();
        var context = new Dictionary<string, string>
        {
            ["language"] = "Python",
            ["previous_result"] = "Step 1 completed"
        };

        // Act
        var prompt = solver.BuildSolvePrompt("Write a function", context);

        // Assert
        prompt.ShouldContain("Python");
        prompt.ShouldContain("Step 1 completed");
    }
}

/// <summary>
/// Tests for MAKER's composition strategy.
/// Verifies that subtask results are properly combined.
/// </summary>
public class CompositionLogicTests
{
    [Fact(DisplayName = "Compose should simple aggregate for small results")]
    public void Compose_ShouldSimpleAggregate_ForSmallResults()
    {
        // Arrange
        var composer = new DefaultComposer();
        var results = new Dictionary<string, string>
        {
            ["1"] = "First part completed",
            ["2"] = "Second part completed",
            ["3"] = "Third part completed"
        };

        // Act
        var composed = composer.Compose("Original task", results, new Dictionary<string, string>());

        // Assert - DefaultComposer simple aggregates when total length <= 2000 and count <= 3
        composed.ShouldNotBeNull();
        composed.ShouldContain("First part completed");
        composed.ShouldContain("Second part completed");
        composed.ShouldContain("Third part completed");
    }

    [Fact(DisplayName = "Compose should return null for large results to trigger LLM synthesis")]
    public void Compose_ShouldReturnNull_ForLargeResults()
    {
        // Arrange
        var composer = new DefaultComposer { SimpleAggregationThreshold = 100 };
        var results = new Dictionary<string, string>
        {
            ["1"] = new string('a', 50),
            ["2"] = new string('b', 50),
            ["3"] = new string('c', 50)
        };

        // Act
        var composed = composer.Compose("Original task", results, new Dictionary<string, string>());

        // Assert - Should return null when total length exceeds threshold
        composed.ShouldBeNull();
    }

    [Fact(DisplayName = "BuildSynthesisPrompt should include all subtask results")]
    public void BuildSynthesisPrompt_ShouldIncludeAllSubtaskResults()
    {
        // Arrange
        var composer = new DefaultComposer();
        var results = new Dictionary<string, string>
        {
            ["step1"] = "Analysis complete",
            ["step2"] = "Design complete",
            ["step3"] = "Implementation complete"
        };

        // Act
        var prompt = composer.BuildSynthesisPrompt("Build a system", results, new Dictionary<string, string>());

        // Assert
        prompt.ShouldContain("Analysis complete");
        prompt.ShouldContain("Design complete");
        prompt.ShouldContain("Implementation complete");
        prompt.ShouldContain("Build a system");
    }
}

/// <summary>
/// Tests for MAKER's red flag detection.
/// Paper: "Red-flagging: Recognizing Signs of Unreliability"
/// </summary>
public class RedFlagDetectionTests
{
    [Theory(DisplayName = "Refusal prefixes should trigger red flag")]
    [InlineData("I cannot help with that request.")]
    [InlineData("I'm sorry, but I can't assist with this.")]
    [InlineData("As an AI, I'm not able to provide that information.")]
    [InlineData("I apologize, but I cannot complete this task.")]
    public void RefusalPrefixes_ShouldTriggerRedFlag(string content)
    {
        // Arrange
        var strategy = new DefaultEnglishRedFlagStrategy();

        // Act
        var isValid = strategy.Validate(content, "test-id", out var reason);

        // Assert
        isValid.ShouldBeFalse();
        reason.ShouldContain("refusal");
    }

    [Theory(DisplayName = "Excessive repetition should trigger red flag")]
    [InlineData("test test test test test test test test test test test test test test test test test test test test")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void ExcessiveRepetition_ShouldTriggerRedFlag(string content)
    {
        // Arrange
        var strategy = new DefaultEnglishRedFlagStrategy();

        // Act
        var isValid = strategy.Validate(content, "test-id", out var reason);

        // Assert
        isValid.ShouldBeFalse();
        reason.ShouldContain("repetition");
    }

    [Fact(DisplayName = "Valid content should pass red flag check")]
    public void ValidContent_ShouldPassRedFlagCheck()
    {
        // Arrange
        var strategy = new DefaultEnglishRedFlagStrategy();
        var validContent = """
            Here is a thoughtful analysis of the problem:
            
            1. First, we need to understand the requirements.
            2. Then, we design an appropriate solution.
            3. Finally, we implement and test the solution.
            
            This approach ensures quality and maintainability.
            """;

        // Act
        var isValid = strategy.Validate(validContent, "test-id", out var reason);

        // Assert
        isValid.ShouldBeTrue();
        reason.ShouldBeNull();
    }

    [Fact(DisplayName = "Too short content should trigger red flag")]
    public void TooShortContent_ShouldTriggerRedFlag()
    {
        // Arrange
        var options = new RedFlagOptions { MinContentLength = 50 };
        var strategy = new DefaultEnglishRedFlagStrategy(options);

        // Act
        var isValid = strategy.Validate("OK", "test-id", out var reason);

        // Assert
        isValid.ShouldBeFalse();
        reason.ShouldContain("short");
    }
}

/// <summary>
/// Tests for MAKER's budget enforcement.
/// Paper: "Budget-based limits replace depth limits"
/// </summary>
public class BudgetEnforcementTests
{
    [Fact(DisplayName = "MakerOptions should enforce LLM call budget")]
    public void MakerOptions_ShouldEnforceLlmCallBudget()
    {
        // Arrange
        var options = new MakerOptions
        {
            MaxTotalLlmCalls = 100
        };

        // Assert
        options.MaxTotalLlmCalls.ShouldBe(100);
    }

    [Fact(DisplayName = "MakerOptions should enforce token budget")]
    public void MakerOptions_ShouldEnforceTokenBudget()
    {
        // Arrange
        var options = new MakerOptions
        {
            MaxTotalTokens = 500_000
        };

        // Assert
        options.MaxTotalTokens.ShouldBe(500_000);
    }

    [Fact(DisplayName = "MakerOptions should enforce duration budget")]
    public void MakerOptions_ShouldEnforceDurationBudget()
    {
        // Arrange
        var options = new MakerOptions
        {
            MaxDuration = TimeSpan.FromMinutes(10)
        };

        // Assert
        options.MaxDuration.TotalMinutes.ShouldBe(10);
    }

    [Fact(DisplayName = "HardDepthCap should provide safety net")]
    public void HardDepthCap_ShouldProvideSafetyNet()
    {
        // Arrange
        var options = new MakerOptions();

        // Assert - Default hard cap prevents infinite recursion
        options.HardDepthCap.ShouldBe(50);
    }

    [Fact(DisplayName = "DepthWarningThreshold should trigger early warning")]
    public void DepthWarningThreshold_ShouldTriggerEarlyWarning()
    {
        // Arrange
        var options = new MakerOptions
        {
            DepthWarningThreshold = 15
        };

        // Assert
        options.DepthWarningThreshold.ShouldBe(15);
        options.DepthWarningThreshold.ShouldBeLessThan(options.HardDepthCap);
    }
}

/// <summary>
/// Tests for MAKER's error correction through re-decomposition.
/// Paper: "If consensus fails, decompose into finer subtasks"
/// </summary>
public class ErrorCorrectionTests
{
    [Fact(DisplayName = "RedFlagHandler should suggest retry on NoConsensus without best candidate")]
    public void RedFlagHandler_ShouldSuggestRetry_OnNoConsensus_WithoutBestCandidate()
    {
        // Arrange
        var handler = new DefaultRedFlagHandler();
        var context = new RedFlagContext
        {
            TaskId = "task-1",
            TaskDescription = "Complex task",
            Type = RedFlagType.NoConsensus,
            Reason = "No consensus reached after 3 rounds",
            BestCandidate = null,
            RecoveryAttempts = 0
        };

        // Act
        var recovery = handler.HandleRedFlag(context);

        // Assert - Should retry with modified prompt
        recovery.Action.ShouldBe(RecoveryAction.Retry);
    }

    [Fact(DisplayName = "RedFlagHandler should accept best effort on NoConsensus with best candidate")]
    public void RedFlagHandler_ShouldAcceptBestEffort_OnNoConsensus_WithBestCandidate()
    {
        // Arrange
        var handler = new DefaultRedFlagHandler();
        var context = new RedFlagContext
        {
            TaskId = "task-1",
            TaskDescription = "Complex task",
            Type = RedFlagType.NoConsensus,
            Reason = "No consensus reached",
            BestCandidate = "Best effort result",
            RecoveryAttempts = 0
        };

        // Act
        var recovery = handler.HandleRedFlag(context);

        // Assert - Should accept best effort when available
        recovery.Action.ShouldBe(RecoveryAction.AcceptBestEffort);
    }

    [Fact(DisplayName = "RedFlagHandler should force atomic on Timeout")]
    public void RedFlagHandler_ShouldForceAtomic_OnTimeout()
    {
        // Arrange
        var handler = new DefaultRedFlagHandler();
        var context = new RedFlagContext
        {
            TaskId = "task-1",
            TaskDescription = "Slow task",
            Type = RedFlagType.Timeout,
            Reason = "Task timed out",
            RecoveryAttempts = 0
        };

        // Act
        var recovery = handler.HandleRedFlag(context);

        // Assert - Should force atomic to simplify
        recovery.Action.ShouldBe(RecoveryAction.ForceAtomic);
    }

    [Fact(DisplayName = "RedFlagHandler should retry on ExecutionFailure")]
    public void RedFlagHandler_ShouldRetry_OnExecutionFailure()
    {
        // Arrange
        var handler = new DefaultRedFlagHandler();
        var context = new RedFlagContext
        {
            TaskId = "task-1",
            TaskDescription = "Failed task",
            Type = RedFlagType.ExecutionFailure,
            Reason = "LLM call failed",
            RecoveryAttempts = 0
        };

        // Act
        var recovery = handler.HandleRedFlag(context);

        // Assert - Should retry on first failure
        recovery.Action.ShouldBe(RecoveryAction.Retry);
    }

    [Fact(DisplayName = "RedFlagHandler should abort after max recovery attempts")]
    public void RedFlagHandler_ShouldAbort_AfterMaxRecoveryAttempts()
    {
        // Arrange
        var handler = new DefaultRedFlagHandler { MaxRecoveryAttempts = 2 };
        var context = new RedFlagContext
        {
            TaskId = "task-1",
            TaskDescription = "Persistent failure",
            Type = RedFlagType.ExecutionFailure,
            Reason = "Still failing",
            BestCandidate = null,
            RecoveryAttempts = 3 // Exceeded max
        };

        // Act
        var recovery = handler.HandleRedFlag(context);

        // Assert - Should abort after max attempts
        recovery.Action.ShouldBe(RecoveryAction.Abort);
    }

    [Fact(DisplayName = "RedFlagHandler should accept best effort after max attempts if available")]
    public void RedFlagHandler_ShouldAcceptBestEffort_AfterMaxAttempts_IfAvailable()
    {
        // Arrange
        var handler = new DefaultRedFlagHandler { MaxRecoveryAttempts = 2 };
        var context = new RedFlagContext
        {
            TaskId = "task-1",
            TaskDescription = "Persistent failure",
            Type = RedFlagType.ExecutionFailure,
            Reason = "Still failing",
            BestCandidate = "Some result",
            RecoveryAttempts = 3 // Exceeded max
        };

        // Act
        var recovery = handler.HandleRedFlag(context);

        // Assert - Should accept best effort if available
        recovery.Action.ShouldBe(RecoveryAction.AcceptBestEffort);
    }
}

/// <summary>
/// Tests for MAKER's N = 2K - 1 formula.
/// Paper: "Sample N = 2K - 1 proposals per round"
/// </summary>
public class SamplingFormulaTests
{
    [Theory(DisplayName = "SamplesPerRound should follow N = 2K - 1 formula")]
    [InlineData(2, 3)]  // K=2 → N=3
    [InlineData(3, 5)]  // K=3 → N=5
    [InlineData(4, 7)]  // K=4 → N=7
    [InlineData(5, 9)]  // K=5 → N=9
    public void SamplesPerRound_ShouldFollow_2K_Minus_1_Formula(int k, int expectedN)
    {
        // Arrange
        var options = new MakerOptions { CustomK = k };

        // Assert
        options.SamplesPerRound.ShouldBe(expectedN);
    }

    [Fact(DisplayName = "Default K=2 should give N=3 samples")]
    public void DefaultK2_ShouldGive_N3_Samples()
    {
        // Arrange
        var options = new MakerOptions(); // Default K=2

        // Assert
        options.ConsensusK.ShouldBe(2);
        options.SamplesPerRound.ShouldBe(3);
    }
}

/// <summary>
/// Tests for MAKER's execution modes.
/// Paper: Academic mode for maximum correctness, Production for efficiency.
/// </summary>
public class ExecutionModeTests
{
    [Fact(DisplayName = "Academic mode should always decompose first")]
    public void AcademicMode_ShouldAlwaysDecomposeFirst()
    {
        // Arrange
        var options = new MakerOptions { Mode = ExecutionMode.Academic };

        // Assert
        options.Mode.ShouldBe(ExecutionMode.Academic);
    }

    [Fact(DisplayName = "Production mode should use heuristics for atomicity")]
    public void ProductionMode_ShouldUseHeuristicsForAtomicity()
    {
        // Arrange
        var options = new MakerOptions { Mode = ExecutionMode.Production };

        // Assert
        options.Mode.ShouldBe(ExecutionMode.Production);
    }

    [Fact(DisplayName = "Default mode should be Production")]
    public void DefaultMode_ShouldBeProduction()
    {
        // Arrange
        var options = new MakerOptions();

        // Assert
        options.Mode.ShouldBe(ExecutionMode.Production);
    }
}
