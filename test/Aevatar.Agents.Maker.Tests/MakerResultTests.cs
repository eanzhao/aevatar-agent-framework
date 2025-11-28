using Shouldly;
using Xunit;

namespace Aevatar.Agents.Maker.Tests;

// ============================================================
//  MakerResult Tests - Result and Trace Structures
//  Paper Reference: Execution trace and result reporting
// ============================================================

public class MakerResultTests
{
    // ============================================================
    //  MakerResult Tests
    // ============================================================

    [Fact(DisplayName = "Successful result structure")]
    public void MakerResult_SuccessfulResult()
    {
        var trace = CreateMinimalTrace();
        var result = new MakerResult
        {
            Success = true,
            Content = "Final output",
            Trace = trace
        };

        result.Success.ShouldBeTrue();
        result.Content.ShouldBe("Final output");
        result.Error.ShouldBeNull();
    }

    [Fact(DisplayName = "Failed result structure")]
    public void MakerResult_FailedResult()
    {
        var trace = CreateMinimalTrace();
        var result = new MakerResult
        {
            Success = false,
            Content = "",
            Trace = trace,
            Error = "Task failed due to timeout"
        };

        result.Success.ShouldBeFalse();
        result.Error.ShouldBe("Task failed due to timeout");
    }

    [Fact(DisplayName = "Computed properties delegate to Trace")]
    public void MakerResult_ComputedProperties_DelegateToTrace()
    {
        var trace = new MakerTrace
        {
            ExecutionId = "E1",
            RootTask = CreateMinimalTaskNode(),
            TotalLLMCalls = 42,
            Duration = TimeSpan.FromSeconds(30),
            TotalTokens = 10000,
            PromptTokens = 7000,
            CompletionTokens = 3000
        };

        var result = new MakerResult
        {
            Success = true,
            Content = "Output",
            Trace = trace
        };

        result.TotalLLMCalls.ShouldBe(42);
        result.Duration.ShouldBe(TimeSpan.FromSeconds(30));
        result.TotalTokens.ShouldBe(10000);
        result.PromptTokens.ShouldBe(7000);
        result.CompletionTokens.ShouldBe(3000);
    }

    // ============================================================
    //  MakerTrace Tests
    // ============================================================

    [Fact(DisplayName = "MakerTrace required properties")]
    public void MakerTrace_RequiredProperties()
    {
        var trace = new MakerTrace
        {
            ExecutionId = "exec-123",
            RootTask = CreateMinimalTaskNode()
        };

        trace.ExecutionId.ShouldBe("exec-123");
        trace.RootTask.ShouldNotBeNull();
    }

    [Fact(DisplayName = "MakerTrace default values")]
    public void MakerTrace_DefaultValues()
    {
        var trace = new MakerTrace
        {
            ExecutionId = "E1",
            RootTask = CreateMinimalTaskNode()
        };

        trace.TotalLLMCalls.ShouldBe(0);
        trace.Duration.ShouldBe(TimeSpan.Zero);
        trace.RedFlags.ShouldBeEmpty();
        trace.TotalTokens.ShouldBe(0);
        trace.PromptTokens.ShouldBe(0);
        trace.CompletionTokens.ShouldBe(0);
    }

    [Fact(DisplayName = "MakerTrace with red flags")]
    public void MakerTrace_WithRedFlags()
    {
        var redFlags = new List<RedFlagEvent>
        {
            new()
            {
                TaskId = "T1",
                Reason = "No consensus",
                Recovered = true
            },
            new()
            {
                TaskId = "T2",
                Reason = "Timeout",
                Recovered = false
            }
        };

        var trace = new MakerTrace
        {
            ExecutionId = "E1",
            RootTask = CreateMinimalTaskNode(),
            RedFlags = redFlags
        };

        trace.RedFlags.Count.ShouldBe(2);
        trace.RedFlags[0].Recovered.ShouldBeTrue();
        trace.RedFlags[1].Recovered.ShouldBeFalse();
    }

    // ============================================================
    //  TaskNode Tests
    // ============================================================

    [Fact(DisplayName = "TaskNode required properties")]
    public void TaskNode_RequiredProperties()
    {
        var node = new TaskNode
        {
            TaskId = "T1",
            Description = "Analyze data"
        };

        node.TaskId.ShouldBe("T1");
        node.Description.ShouldBe("Analyze data");
    }

    [Fact(DisplayName = "TaskNode default values")]
    public void TaskNode_DefaultValues()
    {
        var node = new TaskNode
        {
            TaskId = "T1",
            Description = "Task"
        };

        node.Depth.ShouldBe(0);
        node.IsAtomic.ShouldBeFalse();
        node.VotingSessions.ShouldBeEmpty();
        node.Children.ShouldBeEmpty();
        node.Result.ShouldBeNull();
        node.FallbackCandidate.ShouldBeNull();
    }

    [Fact(DisplayName = "TaskNode with children")]
    public void TaskNode_WithChildren()
    {
        var child1 = new TaskNode { TaskId = "T1.1", Description = "Subtask 1", Depth = 1 };
        var child2 = new TaskNode { TaskId = "T1.2", Description = "Subtask 2", Depth = 1 };

        var parent = new TaskNode
        {
            TaskId = "T1",
            Description = "Parent task",
            Depth = 0,
            Children = [child1, child2]
        };

        parent.Children.Count.ShouldBe(2);
        parent.Children[0].TaskId.ShouldBe("T1.1");
        parent.Children[1].TaskId.ShouldBe("T1.2");
    }

    [Fact(DisplayName = "Atomic TaskNode with result")]
    public void TaskNode_AtomicWithResult()
    {
        var node = new TaskNode
        {
            TaskId = "T1",
            Description = "Simple task",
            IsAtomic = true,
            Result = "The answer is 42"
        };

        node.IsAtomic.ShouldBeTrue();
        node.Result.ShouldBe("The answer is 42");
    }

    [Fact(DisplayName = "TaskNode with voting sessions")]
    public void TaskNode_WithVotingSessions()
    {
        var sessions = new List<VotingSession>
        {
            new()
            {
                Type = VotingType.Decomposition,
                Rounds = 2,
                Winner = new VoteCandidate { Hash = "ABC", Content = "Winner", Votes = 3 }
            }
        };

        var node = new TaskNode
        {
            TaskId = "T1",
            Description = "Task",
            VotingSessions = sessions
        };

        node.VotingSessions.Count.ShouldBe(1);
        node.VotingSessions[0].Type.ShouldBe(VotingType.Decomposition);
        node.VotingSessions[0].Winner.ShouldNotBeNull();
    }

    // ============================================================
    //  VotingSession Tests
    // ============================================================

    [Fact(DisplayName = "VotingSession required properties")]
    public void VotingSession_RequiredProperties()
    {
        var session = new VotingSession
        {
            Type = VotingType.Solution
        };

        session.Type.ShouldBe(VotingType.Solution);
    }

    [Fact(DisplayName = "VotingSession default values")]
    public void VotingSession_DefaultValues()
    {
        var session = new VotingSession
        {
            Type = VotingType.Decomposition
        };

        session.Candidates.ShouldBeEmpty();
        session.Winner.ShouldBeNull();
        session.Rounds.ShouldBe(0);
    }

    [Fact(DisplayName = "VotingSession with candidates")]
    public void VotingSession_WithCandidates()
    {
        var candidates = new List<VoteCandidate>
        {
            new() { Hash = "A1", Content = "Option A", Votes = 3 },
            new() { Hash = "B2", Content = "Option B", Votes = 2 },
            new() { Hash = "C3", Content = "Option C", Votes = 1 }
        };

        var session = new VotingSession
        {
            Type = VotingType.Solution,
            Candidates = candidates,
            Winner = candidates[0],
            Rounds = 2
        };

        session.Candidates.Count.ShouldBe(3);
        session.Winner!.Content.ShouldBe("Option A");
        session.Rounds.ShouldBe(2);
    }

    // ============================================================
    //  RedFlagEvent Tests
    // ============================================================

    [Fact(DisplayName = "RedFlagEvent required properties")]
    public void RedFlagEvent_RequiredProperties()
    {
        var evt = new RedFlagEvent
        {
            TaskId = "T1",
            Reason = "Voting timeout"
        };

        evt.TaskId.ShouldBe("T1");
        evt.Reason.ShouldBe("Voting timeout");
    }

    [Fact(DisplayName = "RedFlagEvent default values")]
    public void RedFlagEvent_DefaultValues()
    {
        var before = DateTimeOffset.UtcNow;
        var evt = new RedFlagEvent
        {
            TaskId = "T1",
            Reason = "Reason"
        };
        var after = DateTimeOffset.UtcNow;

        evt.Recovered.ShouldBeFalse();
        evt.Timestamp.ShouldBeInRange(before, after);
    }

    [Fact(DisplayName = "RedFlagEvent with recovery")]
    public void RedFlagEvent_RecoveredTrue()
    {
        var evt = new RedFlagEvent
        {
            TaskId = "T1",
            Reason = "Temporary failure",
            Recovered = true
        };

        evt.Recovered.ShouldBeTrue();
    }

    // ============================================================
    //  VoteCandidate Tests
    // ============================================================

    [Fact(DisplayName = "VoteCandidate required properties")]
    public void VoteCandidate_RequiredProperties()
    {
        var candidate = new VoteCandidate
        {
            Hash = "ABC123",
            Content = "The solution",
            Votes = 5
        };

        candidate.Hash.ShouldBe("ABC123");
        candidate.Content.ShouldBe("The solution");
        candidate.Votes.ShouldBe(5);
    }

    [Fact(DisplayName = "VoteCandidate ClusterSize default is 1")]
    public void VoteCandidate_ClusterSizeDefault()
    {
        var candidate = new VoteCandidate
        {
            Hash = "X",
            Content = "C",
            Votes = 1
        };

        candidate.ClusterSize.ShouldBe(1);
    }

    [Fact(DisplayName = "VoteCandidate with cluster size")]
    public void VoteCandidate_WithClusterSize()
    {
        var candidate = new VoteCandidate
        {
            Hash = "X",
            Content = "C",
            Votes = 5,
            ClusterSize = 3
        };

        candidate.ClusterSize.ShouldBe(3);
    }

    // ============================================================
    //  Helper Methods
    // ============================================================

    private static MakerTrace CreateMinimalTrace() => new()
    {
        ExecutionId = "test-exec",
        RootTask = CreateMinimalTaskNode()
    };

    private static TaskNode CreateMinimalTaskNode() => new()
    {
        TaskId = "root",
        Description = "Root task"
    };
}
