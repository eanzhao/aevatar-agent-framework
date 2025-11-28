using Shouldly;
using Xunit;

namespace Aevatar.Agents.Maker.Tests;

// ============================================================
//  MakerProgress Tests - Progress Reporting Structures
//  Paper Reference: Real-time progress monitoring during execution
// ============================================================

public class MakerProgressTests
{
    // ============================================================
    //  MakerProgress Tests
    // ============================================================

    [Fact(DisplayName = "MakerProgress required properties")]
    public void MakerProgress_RequiredProperties()
    {
        var progress = new MakerProgress
        {
            Phase = MakerPhase.Decomposing,
            TaskId = "T1",
            Message = "Breaking down task"
        };

        progress.Phase.ShouldBe(MakerPhase.Decomposing);
        progress.TaskId.ShouldBe("T1");
        progress.Message.ShouldBe("Breaking down task");
    }

    [Fact(DisplayName = "MakerProgress default values")]
    public void MakerProgress_DefaultValues()
    {
        var before = DateTimeOffset.UtcNow;
        var progress = new MakerProgress
        {
            Phase = MakerPhase.Starting,
            TaskId = "T1",
            Message = "Starting"
        };
        var after = DateTimeOffset.UtcNow;

        progress.Depth.ShouldBe(0);
        progress.Voting.ShouldBeNull();
        progress.Proposal.ShouldBeNull();
        progress.StreamingToken.ShouldBeNull();
        progress.Timestamp.ShouldBeInRange(before, after);
    }

    [Fact(DisplayName = "MakerProgress with voting progress")]
    public void MakerProgress_WithVotingProgress()
    {
        var voting = new VotingProgress
        {
            Type = VotingType.Solution,
            Round = 2,
            TotalVotes = 5,
            VotesNeeded = 3,
            LeaderVotes = 3,
            RunnerUpVotes = 2
        };

        var progress = new MakerProgress
        {
            Phase = MakerPhase.Voting,
            TaskId = "T1",
            Message = "Voting in progress",
            Voting = voting
        };

        progress.Voting.ShouldNotBeNull();
        progress.Voting.Type.ShouldBe(VotingType.Solution);
        progress.Voting.LeaderVotes.ShouldBe(3);
    }

    [Fact(DisplayName = "MakerProgress with LLM proposal")]
    public void MakerProgress_WithProposal()
    {
        var proposal = new LLMProposal
        {
            ProposalId = "P1",
            Content = "Generated content",
            Success = true,
            PromptTokens = 100,
            CompletionTokens = 50
        };

        var progress = new MakerProgress
        {
            Phase = MakerPhase.Solving,
            TaskId = "T1",
            Message = "Received proposal",
            Proposal = proposal
        };

        progress.Proposal.ShouldNotBeNull();
        progress.Proposal.ProposalId.ShouldBe("P1");
        progress.Proposal.TotalTokens.ShouldBe(150);
    }

    // ============================================================
    //  MakerPhase Enum Tests
    // ============================================================

    [Fact(DisplayName = "MakerPhase has all expected values")]
    public void MakerPhase_AllValuesExist()
    {
        var phases = Enum.GetValues<MakerPhase>();
        phases.Length.ShouldBe(11);
    }

    [Theory(DisplayName = "MakerPhase valid values")]
    [InlineData(MakerPhase.Starting)]
    [InlineData(MakerPhase.Assessing)]
    [InlineData(MakerPhase.Decomposing)]
    [InlineData(MakerPhase.Voting)]
    [InlineData(MakerPhase.Executing)]
    [InlineData(MakerPhase.Solving)]
    [InlineData(MakerPhase.Composing)]
    [InlineData(MakerPhase.RedFlag)]
    [InlineData(MakerPhase.Streaming)]
    [InlineData(MakerPhase.Completed)]
    [InlineData(MakerPhase.Failed)]
    public void MakerPhase_ValidValues(MakerPhase phase)
    {
        Enum.IsDefined(phase).ShouldBeTrue();
    }

    // ============================================================
    //  VotingProgress Tests
    // ============================================================

    [Fact(DisplayName = "VotingProgress required properties")]
    public void VotingProgress_RequiredProperties()
    {
        var progress = new VotingProgress
        {
            Type = VotingType.Decomposition
        };

        progress.Type.ShouldBe(VotingType.Decomposition);
    }

    [Fact(DisplayName = "VotingProgress default values")]
    public void VotingProgress_DefaultValues()
    {
        var progress = new VotingProgress
        {
            Type = VotingType.Solution
        };

        progress.Round.ShouldBe(0);
        progress.TotalVotes.ShouldBe(0);
        progress.VotesNeeded.ShouldBe(0);
        progress.LeaderVotes.ShouldBe(0);
        progress.RunnerUpVotes.ShouldBe(0);
        progress.ClusterCount.ShouldBe(0);
        progress.UsedSemanticClustering.ShouldBeFalse();
    }

    [Fact(DisplayName = "VotingProgress fully populated")]
    public void VotingProgress_FullyPopulated()
    {
        var progress = new VotingProgress
        {
            Type = VotingType.Solution,
            Round = 3,
            TotalVotes = 9,
            VotesNeeded = 3,
            LeaderVotes = 5,
            RunnerUpVotes = 3,
            ClusterCount = 4,
            UsedSemanticClustering = true
        };

        progress.Round.ShouldBe(3);
        progress.TotalVotes.ShouldBe(9);
        progress.VotesNeeded.ShouldBe(3);
        progress.LeaderVotes.ShouldBe(5);
        progress.RunnerUpVotes.ShouldBe(3);
        progress.ClusterCount.ShouldBe(4);
        progress.UsedSemanticClustering.ShouldBeTrue();
    }

    // ============================================================
    //  VotingType Enum Tests
    // ============================================================

    [Fact(DisplayName = "VotingType has all expected values")]
    public void VotingType_AllValuesExist()
    {
        var types = Enum.GetValues<VotingType>();
        types.Length.ShouldBe(2);
    }

    [Fact(DisplayName = "VotingType values")]
    public void VotingType_Values()
    {
        ((int)VotingType.Decomposition).ShouldBe(0);
        ((int)VotingType.Solution).ShouldBe(1);
    }

    // ============================================================
    //  LLMProposal Tests
    // ============================================================

    [Fact(DisplayName = "LLMProposal required properties")]
    public void LLMProposal_RequiredProperties()
    {
        var proposal = new LLMProposal
        {
            ProposalId = "P1",
            Content = "Generated response"
        };

        proposal.ProposalId.ShouldBe("P1");
        proposal.Content.ShouldBe("Generated response");
    }

    [Fact(DisplayName = "LLMProposal default values")]
    public void LLMProposal_DefaultValues()
    {
        var proposal = new LLMProposal
        {
            ProposalId = "P1",
            Content = "Content"
        };

        proposal.Success.ShouldBeFalse();
        proposal.Error.ShouldBeNull();
        proposal.PromptTokens.ShouldBe(0);
        proposal.CompletionTokens.ShouldBe(0);
        proposal.LatencyMs.ShouldBe(0);
        proposal.ProviderName.ShouldBeNull();
    }

    [Fact(DisplayName = "LLMProposal TotalTokens is computed")]
    public void LLMProposal_TotalTokens_IsComputed()
    {
        var proposal = new LLMProposal
        {
            ProposalId = "P1",
            Content = "Content",
            PromptTokens = 100,
            CompletionTokens = 50
        };

        proposal.TotalTokens.ShouldBe(150);
    }

    [Fact(DisplayName = "LLMProposal successful")]
    public void LLMProposal_SuccessfulProposal()
    {
        var proposal = new LLMProposal
        {
            ProposalId = "P1",
            Content = "The answer is 42",
            Success = true,
            PromptTokens = 50,
            CompletionTokens = 10,
            LatencyMs = 250,
            ProviderName = "gpt-4"
        };

        proposal.Success.ShouldBeTrue();
        proposal.Error.ShouldBeNull();
        proposal.LatencyMs.ShouldBe(250);
        proposal.ProviderName.ShouldBe("gpt-4");
    }

    [Fact(DisplayName = "LLMProposal failed")]
    public void LLMProposal_FailedProposal()
    {
        var proposal = new LLMProposal
        {
            ProposalId = "P1",
            Content = "",
            Success = false,
            Error = "API rate limit exceeded"
        };

        proposal.Success.ShouldBeFalse();
        proposal.Error.ShouldBe("API rate limit exceeded");
    }

    // ============================================================
    //  StreamingTokenProgress Tests
    // ============================================================

    [Fact(DisplayName = "StreamingTokenProgress required properties")]
    public void StreamingTokenProgress_RequiredProperties()
    {
        var token = new StreamingTokenProgress
        {
            WorkerId = "W1",
            ProposalId = "P1",
            Token = "Hello",
            AccumulatedContent = "Hello"
        };

        token.WorkerId.ShouldBe("W1");
        token.ProposalId.ShouldBe("P1");
        token.Token.ShouldBe("Hello");
        token.AccumulatedContent.ShouldBe("Hello");
    }

    [Fact(DisplayName = "StreamingTokenProgress default values")]
    public void StreamingTokenProgress_DefaultValues()
    {
        var token = new StreamingTokenProgress
        {
            WorkerId = "W1",
            ProposalId = "P1",
            Token = "T",
            AccumulatedContent = "T"
        };

        token.TokenIndex.ShouldBe(0);
        token.SystemPrompt.ShouldBeNull();
        token.UserPrompt.ShouldBeNull();
        token.IsFirstToken.ShouldBeFalse();
        token.IsLastToken.ShouldBeFalse();
        token.ProviderName.ShouldBeNull();
    }

    [Fact(DisplayName = "StreamingTokenProgress first token")]
    public void StreamingTokenProgress_FirstToken()
    {
        var token = new StreamingTokenProgress
        {
            WorkerId = "W1",
            ProposalId = "P1",
            Token = "The",
            AccumulatedContent = "The",
            TokenIndex = 0,
            IsFirstToken = true,
            SystemPrompt = "You are a helpful assistant",
            UserPrompt = "What is 2+2?"
        };

        token.IsFirstToken.ShouldBeTrue();
        token.IsLastToken.ShouldBeFalse();
        token.SystemPrompt.ShouldNotBeNull();
        token.UserPrompt.ShouldNotBeNull();
    }

    [Fact(DisplayName = "StreamingTokenProgress last token")]
    public void StreamingTokenProgress_LastToken()
    {
        var token = new StreamingTokenProgress
        {
            WorkerId = "W1",
            ProposalId = "P1",
            Token = ".",
            AccumulatedContent = "The answer is 4.",
            TokenIndex = 5,
            IsFirstToken = false,
            IsLastToken = true,
            ProviderName = "deepseek"
        };

        token.IsFirstToken.ShouldBeFalse();
        token.IsLastToken.ShouldBeTrue();
        token.TokenIndex.ShouldBe(5);
        token.ProviderName.ShouldBe("deepseek");
    }
}
