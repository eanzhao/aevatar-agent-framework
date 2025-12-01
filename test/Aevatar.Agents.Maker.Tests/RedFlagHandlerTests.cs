using Shouldly;
using Xunit;

namespace Aevatar.Agents.Maker.Tests;

// ============================================================
//  RedFlagHandler Tests - Error Recovery Logic
//  Paper Reference: Section 3.3 Red-Flagging and recovery strategies
// ============================================================

public class RedFlagHandlerTests
{
    private readonly DefaultRedFlagHandler _handler = new();

    // ============================================================
    //  NoConsensus Recovery Tests
    // ============================================================

    [Fact(DisplayName = "NoConsensus with best candidate: accept best effort")]
    public void HandleRedFlag_NoConsensus_WithBestCandidate_ShouldAcceptBestEffort()
    {
        var context = new RedFlagContext
        {
            TaskId = "T1",
            TaskDescription = "Some task",
            Type = RedFlagType.NoConsensus,
            Reason = "Voting failed",
            BestCandidate = "Best answer so far"
        };

        var recovery = _handler.HandleRedFlag(context);

        recovery.Action.ShouldBe(RecoveryAction.AcceptBestEffort);
        recovery.Message.ShouldContain("leading candidate");
    }

    [Fact(DisplayName = "NoConsensus without best candidate: retry with modified prompt")]
    public void HandleRedFlag_NoConsensus_WithoutBestCandidate_ShouldRetry()
    {
        var context = new RedFlagContext
        {
            TaskId = "T1",
            TaskDescription = "Original task",
            Type = RedFlagType.NoConsensus,
            Reason = "Voting failed",
            BestCandidate = null
        };

        var recovery = _handler.HandleRedFlag(context);

        recovery.Action.ShouldBe(RecoveryAction.Retry);
        recovery.ModifiedPrompt.ShouldContain("IMPORTANT");
        recovery.ModifiedPrompt.ShouldContain("DETERMINISTIC");
        recovery.ModifiedPrompt.ShouldContain("Original task");
    }

    // ============================================================
    //  Timeout Recovery Tests
    // ============================================================

    [Fact(DisplayName = "Timeout: force atomic")]
    public void HandleRedFlag_Timeout_ShouldForceAtomic()
    {
        var context = new RedFlagContext
        {
            TaskId = "T1",
            TaskDescription = "Task",
            Type = RedFlagType.Timeout,
            Reason = "Step timeout exceeded"
        };

        var recovery = _handler.HandleRedFlag(context);

        recovery.Action.ShouldBe(RecoveryAction.ForceAtomic);
        recovery.Message.ShouldContain("Timeout");
    }

    // ============================================================
    //  ExecutionFailure Recovery Tests
    // ============================================================

    [Fact(DisplayName = "ExecutionFailure: retry")]
    public void HandleRedFlag_ExecutionFailure_ShouldRetry()
    {
        var context = new RedFlagContext
        {
            TaskId = "T1",
            TaskDescription = "Task",
            Type = RedFlagType.ExecutionFailure,
            Reason = "LLM API error"
        };

        var recovery = _handler.HandleRedFlag(context);

        recovery.Action.ShouldBe(RecoveryAction.Retry);
        recovery.Message.ShouldContain("Retrying");
    }

    // ============================================================
    //  InvalidDecomposition Recovery Tests
    // ============================================================

    [Fact(DisplayName = "InvalidDecomposition: force atomic")]
    public void HandleRedFlag_InvalidDecomposition_ShouldForceAtomic()
    {
        var context = new RedFlagContext
        {
            TaskId = "T1",
            TaskDescription = "Task",
            Type = RedFlagType.InvalidDecomposition,
            Reason = "No valid steps produced"
        };

        var recovery = _handler.HandleRedFlag(context);

        recovery.Action.ShouldBe(RecoveryAction.ForceAtomic);
        recovery.Message.ShouldContain("Decomposition failed");
    }

    // ============================================================
    //  DepthExceeded Recovery Tests
    // ============================================================

    [Fact(DisplayName = "DepthExceeded: force atomic")]
    public void HandleRedFlag_DepthExceeded_ShouldForceAtomic()
    {
        var context = new RedFlagContext
        {
            TaskId = "T1",
            TaskDescription = "Task",
            Type = RedFlagType.DepthExceeded,
            Reason = "Maximum depth reached",
            Depth = 50
        };

        var recovery = _handler.HandleRedFlag(context);

        recovery.Action.ShouldBe(RecoveryAction.ForceAtomic);
        recovery.Message.ShouldContain("depth exceeded");
    }

    // ============================================================
    //  Max Recovery Attempts Tests
    // ============================================================

    [Fact(DisplayName = "Max attempts with best candidate: accept best effort")]
    public void HandleRedFlag_MaxRecoveryAttempts_WithBestCandidate_ShouldAcceptBestEffort()
    {
        var handler = new DefaultRedFlagHandler { MaxRecoveryAttempts = 2 };
        var context = new RedFlagContext
        {
            TaskId = "T1",
            TaskDescription = "Task",
            Type = RedFlagType.NoConsensus,
            Reason = "Still no consensus",
            RecoveryAttempts = 2,
            BestCandidate = "Some answer"
        };

        var recovery = handler.HandleRedFlag(context);

        recovery.Action.ShouldBe(RecoveryAction.AcceptBestEffort);
        recovery.Message.ShouldContain("2 recovery attempts");
    }

    [Fact(DisplayName = "Max attempts without best candidate: abort")]
    public void HandleRedFlag_MaxRecoveryAttempts_WithoutBestCandidate_ShouldAbort()
    {
        var handler = new DefaultRedFlagHandler { MaxRecoveryAttempts = 2 };
        var context = new RedFlagContext
        {
            TaskId = "T1",
            TaskDescription = "Task",
            Type = RedFlagType.ExecutionFailure,
            Reason = "Repeated failures",
            RecoveryAttempts = 3,
            BestCandidate = null
        };

        var recovery = handler.HandleRedFlag(context);

        recovery.Action.ShouldBe(RecoveryAction.Abort);
        recovery.Message.ShouldContain("failed recovery attempts");
    }

    [Fact(DisplayName = "Below max attempts: continue normal recovery")]
    public void HandleRedFlag_BelowMaxAttempts_ShouldContinueNormalRecovery()
    {
        var handler = new DefaultRedFlagHandler { MaxRecoveryAttempts = 3 };
        var context = new RedFlagContext
        {
            TaskId = "T1",
            TaskDescription = "Task",
            Type = RedFlagType.ExecutionFailure,
            Reason = "API error",
            RecoveryAttempts = 1
        };

        var recovery = handler.HandleRedFlag(context);

        recovery.Action.ShouldBe(RecoveryAction.Retry);
    }

    // ============================================================
    //  Custom MaxRecoveryAttempts Tests
    // ============================================================

    [Fact(DisplayName = "MaxRecoveryAttempts can be customized")]
    public void MaxRecoveryAttempts_CanBeCustomized()
    {
        var handler = new DefaultRedFlagHandler { MaxRecoveryAttempts = 5 };

        handler.MaxRecoveryAttempts.ShouldBe(5);
    }

    [Fact(DisplayName = "MaxRecoveryAttempts default is 2")]
    public void MaxRecoveryAttempts_DefaultIs2()
    {
        var handler = new DefaultRedFlagHandler();

        handler.MaxRecoveryAttempts.ShouldBe(2);
    }

    // ============================================================
    //  RedFlagContext Tests
    // ============================================================

    [Fact(DisplayName = "RedFlagContext required properties")]
    public void RedFlagContext_RequiredProperties()
    {
        var context = new RedFlagContext
        {
            TaskId = "T1",
            TaskDescription = "Test task",
            Type = RedFlagType.NoConsensus,
            Reason = "Test reason"
        };

        context.TaskId.ShouldBe("T1");
        context.TaskDescription.ShouldBe("Test task");
        context.Type.ShouldBe(RedFlagType.NoConsensus);
        context.Reason.ShouldBe("Test reason");
    }

    [Fact(DisplayName = "RedFlagContext optional properties have defaults")]
    public void RedFlagContext_OptionalProperties_HaveDefaults()
    {
        var context = new RedFlagContext
        {
            TaskId = "T1",
            TaskDescription = "Task",
            Type = RedFlagType.Timeout,
            Reason = "Reason"
        };

        context.Depth.ShouldBe(0);
        context.RecoveryAttempts.ShouldBe(0);
        context.BestCandidate.ShouldBeNull();
    }

    // ============================================================
    //  RedFlagRecovery Tests
    // ============================================================

    [Fact(DisplayName = "RedFlagRecovery required action")]
    public void RedFlagRecovery_RequiredAction()
    {
        var recovery = new RedFlagRecovery
        {
            Action = RecoveryAction.Retry
        };

        recovery.Action.ShouldBe(RecoveryAction.Retry);
        recovery.ModifiedPrompt.ShouldBeNull();
        recovery.Message.ShouldBeNull();
    }

    [Fact(DisplayName = "RedFlagRecovery all properties set")]
    public void RedFlagRecovery_AllPropertiesSet()
    {
        var recovery = new RedFlagRecovery
        {
            Action = RecoveryAction.Retry,
            ModifiedPrompt = "New prompt",
            Message = "Recovery message"
        };

        recovery.Action.ShouldBe(RecoveryAction.Retry);
        recovery.ModifiedPrompt.ShouldBe("New prompt");
        recovery.Message.ShouldBe("Recovery message");
    }

    // ============================================================
    //  RecoveryAction Enum Tests
    // ============================================================

    [Fact(DisplayName = "RecoveryAction has all expected values")]
    public void RecoveryAction_AllValuesExist()
    {
        Enum.GetValues<RecoveryAction>().Length.ShouldBe(4);
        
        ((int)RecoveryAction.Retry).ShouldBe(0);
        ((int)RecoveryAction.AcceptBestEffort).ShouldBe(1);
        ((int)RecoveryAction.ForceAtomic).ShouldBe(2);
        ((int)RecoveryAction.Abort).ShouldBe(3);
    }

    // ============================================================
    //  RedFlagType Enum Tests
    // ============================================================

    [Fact(DisplayName = "RedFlagType has all expected values")]
    public void RedFlagType_AllValuesExist()
    {
        Enum.GetValues<RedFlagType>().Length.ShouldBe(5);
    }
}
