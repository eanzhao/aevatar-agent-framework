using Shouldly;
using Xunit;

namespace Aevatar.Agents.Maker.Tests;

// ============================================================
//  VoteEngine Tests - First-to-ahead-by-K Consensus Algorithm
//  Paper Reference: Section 3.2 First-to-ahead-by-k Voting and Scaling Laws
// ============================================================

public class VoteEngineTests : IDisposable
{
    private VoteEngine _engine = null!;

    public void Dispose()
    {
        _engine?.Dispose();
    }

    // ============================================================
    //  Basic Consensus Tests
    // ============================================================

    [Fact(DisplayName = "N = 2K - 1: Samples per round follows paper formula")]
    public void SamplesPerRound_ShouldBe_2K_Minus_1()
    {
        // K=1 -> N=1, K=2 -> N=3, K=3 -> N=5
        using var engine1 = new VoteEngine(k: 1);
        using var engine2 = new VoteEngine(k: 2);
        using var engine3 = new VoteEngine(k: 3);

        engine1.SamplesPerRound.ShouldBe(1);
        engine2.SamplesPerRound.ShouldBe(3);
        engine3.SamplesPerRound.ShouldBe(5);
    }

    [Fact(DisplayName = "K=1: First vote reaches consensus immediately")]
    public void SubmitVote_WithK1_ShouldReachConsensusImmediately()
    {
        // K=1 means first vote wins (ahead by 1)
        _engine = new VoteEngine(k: 1);

        var result = _engine.SubmitVote("answer A");

        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.WinningContent.ShouldBe("answer A");
        result.LeaderVotes.ShouldBe(1);
    }

    [Fact(DisplayName = "K=2: Needs leader ahead by 2 votes for consensus")]
    public void SubmitVote_WithK2_ShouldNeedTwoMatchingVotes()
    {
        // K=2: leader needs to be ahead by 2
        _engine = new VoteEngine(k: 2);

        // First vote - no consensus yet
        var result1 = _engine.SubmitVote("answer A");
        result1.ShouldBeNull();

        // Second identical vote - ahead by 2 (2-0 >= 2)
        var result2 = _engine.SubmitVote("answer A");
        result2.ShouldNotBeNull();
        result2.Success.ShouldBeTrue();
        result2.WinningContent.ShouldBe("answer A");
    }

    [Fact(DisplayName = "Competing answers with split votes fail consensus")]
    public void SubmitVote_WithCompetingAnswers_ShouldNotReachConsensus()
    {
        _engine = new VoteEngine(k: 2, maxRounds: 1);

        _engine.SubmitVote("answer A");
        _engine.SubmitVote("answer B");
        var result = _engine.SubmitVote("answer C");

        // 1-1-1 split, no one ahead by 2
        // After 3 votes (N=3 for K=2), round ends without consensus
        result.ShouldNotBeNull();
        result.Success.ShouldBeFalse();
        result.FailureReason.ShouldContain("Max voting rounds");
    }

    [Fact(DisplayName = "Accumulate votes until leader ahead by K")]
    public void SubmitVote_WithTieBreaker_ShouldReachConsensus()
    {
        _engine = new VoteEngine(k: 2);

        _engine.SubmitVote("answer A");
        _engine.SubmitVote("answer B");
        _engine.SubmitVote("answer A"); // A:2, B:1

        // Still not ahead by 2 (2-1=1 < 2)
        var result = _engine.SubmitVote("answer A"); // A:3, B:1

        // Now ahead by 2 (3-1=2 >= 2)
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.WinningContent.ShouldBe("answer A");
    }

    // ============================================================
    //  Edge Cases
    // ============================================================

    [Fact(DisplayName = "Empty content votes are ignored")]
    public void SubmitVote_WithEmptyContent_ShouldReturnNull()
    {
        _engine = new VoteEngine(k: 1);

        var result = _engine.SubmitVote("");
        result.ShouldBeNull();

        var result2 = _engine.SubmitVote("   ");
        result2.ShouldBeNull();
    }

    [Fact(DisplayName = "Content canonicalization: trim whitespace before matching")]
    public void SubmitVote_ShouldCanonicalizeContent()
    {
        // Whitespace should be trimmed
        _engine = new VoteEngine(k: 2);

        _engine.SubmitVote("  answer A  ");
        var result = _engine.SubmitVote("answer A");

        // Should match after trimming
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
    }

    [Fact(DisplayName = "TotalVotes tracks all valid submissions")]
    public void TotalVotes_ShouldTrackAllSubmissions()
    {
        _engine = new VoteEngine(k: 3);

        _engine.SubmitVote("A");
        _engine.SubmitVote("B");
        _engine.SubmitVote("C");

        _engine.TotalVotes.ShouldBe(3);
    }

    [Fact(DisplayName = "Round increments after N votes")]
    public void CurrentRound_ShouldIncrementAfterNVotes()
    {
        _engine = new VoteEngine(k: 2, maxRounds: 3); // N=3

        _engine.CurrentRound.ShouldBe(1);

        _engine.SubmitVote("A");
        _engine.SubmitVote("B");
        _engine.SubmitVote("C"); // End of round 1

        _engine.CurrentRound.ShouldBe(2);
    }

    // ============================================================
    //  GetBestCandidate Tests
    // ============================================================

    [Fact(DisplayName = "GetBestCandidate returns highest vote count candidate")]
    public void GetBestCandidate_ShouldReturnHighestVoteCount()
    {
        _engine = new VoteEngine(k: 3);

        _engine.SubmitVote("A");
        _engine.SubmitVote("B");
        _engine.SubmitVote("A");
        _engine.SubmitVote("C");
        _engine.SubmitVote("A");

        var best = _engine.GetBestCandidate();
        best.ShouldNotBeNull();
        best.Content.ShouldBe("A");
        best.Votes.ShouldBe(3);
    }

    [Fact(DisplayName = "GetBestCandidate returns null when no votes")]
    public void GetBestCandidate_WithNoVotes_ShouldReturnNull()
    {
        _engine = new VoteEngine(k: 2);

        var best = _engine.GetBestCandidate();
        best.ShouldBeNull();
    }

    // ============================================================
    //  GetAllCandidates Tests
    // ============================================================

    [Fact(DisplayName = "GetAllCandidates returns candidates ordered by votes descending")]
    public void GetAllCandidates_ShouldReturnOrderedByVotes()
    {
        _engine = new VoteEngine(k: 3);

        _engine.SubmitVote("A");
        _engine.SubmitVote("B");
        _engine.SubmitVote("A");
        _engine.SubmitVote("B");
        _engine.SubmitVote("C");
        _engine.SubmitVote("A");

        var candidates = _engine.GetAllCandidates();
        candidates.Count.ShouldBe(3);
        candidates[0].Content.ShouldBe("A");
        candidates[0].Votes.ShouldBe(3);
        candidates[1].Content.ShouldBe("B");
        candidates[1].Votes.ShouldBe(2);
        candidates[2].Content.ShouldBe("C");
        candidates[2].Votes.ShouldBe(1);
    }

    // ============================================================
    //  Reset Tests
    // ============================================================

    [Fact(DisplayName = "Reset clears all voting state")]
    public void Reset_ShouldClearAllState()
    {
        _engine = new VoteEngine(k: 2);

        _engine.SubmitVote("A");
        _engine.SubmitVote("A");
        _engine.TotalVotes.ShouldBe(2);

        _engine.Reset();

        _engine.TotalVotes.ShouldBe(0);
        _engine.CurrentRound.ShouldBe(1);
        _engine.GetAllCandidates().ShouldBeEmpty();
    }

    // ============================================================
    //  GetProgress Tests
    // ============================================================

    [Fact(DisplayName = "GetProgress returns current voting state snapshot")]
    public void GetProgress_ShouldReturnCurrentState()
    {
        _engine = new VoteEngine(k: 2);

        _engine.SubmitVote("A");
        _engine.SubmitVote("B");

        var progress = _engine.GetProgress(VotingType.Solution);

        progress.Type.ShouldBe(VotingType.Solution);
        progress.Round.ShouldBe(1);
        progress.TotalVotes.ShouldBe(2);
        progress.VotesNeeded.ShouldBe(2); // K value
        progress.LeaderVotes.ShouldBe(1);
        progress.RunnerUpVotes.ShouldBe(1);
        progress.ClusterCount.ShouldBe(2);
    }

    // ============================================================
    //  Semantic Clustering Tests (without embedding generator)
    // ============================================================

    [Fact(DisplayName = "Semantic clustering disabled without embedding generator")]
    public void UseSemanticClustering_WithoutGenerator_ShouldBeFalse()
    {
        _engine = new VoteEngine(k: 2, embeddingGenerator: null);

        _engine.UseSemanticClustering.ShouldBeFalse();
    }

    // ============================================================
    //  K Value Edge Cases
    // ============================================================

    [Fact(DisplayName = "K=0 defaults to K=1")]
    public void Constructor_WithZeroK_ShouldDefaultToOne()
    {
        _engine = new VoteEngine(k: 0);

        _engine.SamplesPerRound.ShouldBe(1); // 2*1-1 = 1
    }

    [Fact(DisplayName = "Negative K defaults to K=1")]
    public void Constructor_WithNegativeK_ShouldDefaultToOne()
    {
        _engine = new VoteEngine(k: -5);

        _engine.SamplesPerRound.ShouldBe(1);
    }

    // ============================================================
    //  Multi-Round Voting Tests
    // ============================================================

    [Fact(DisplayName = "Multi-round voting continues until consensus")]
    public void MultipleRounds_ShouldContinueUntilConsensus()
    {
        _engine = new VoteEngine(k: 2, maxRounds: 3);

        // Round 1: A, B, C (no consensus)
        _engine.SubmitVote("A");
        _engine.SubmitVote("B");
        _engine.SubmitVote("C");

        // Round 2: A, A, B (A:3, B:2, C:1, still no consensus 3-2=1 < 2)
        _engine.SubmitVote("A");
        _engine.SubmitVote("A");
        _engine.SubmitVote("B");

        // Round 3: A (A:4, B:2, C:1, consensus! 4-2=2 >= 2)
        var result = _engine.SubmitVote("A");

        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.WinningContent.ShouldBe("A");
        result.LeaderVotes.ShouldBe(4);
        result.Rounds.ShouldBe(3);
    }

    // ============================================================
    //  Async Vote Submission Tests
    // ============================================================

    [Fact(DisplayName = "Async vote submission behaves like sync")]
    public async Task SubmitVoteAsync_ShouldWorkLikeSync()
    {
        _engine = new VoteEngine(k: 2);

        await _engine.SubmitVoteAsync("A");
        var result = await _engine.SubmitVoteAsync("A");

        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.WinningContent.ShouldBe("A");
    }

    [Fact(DisplayName = "Async vote submission accepts cancellation token")]
    public async Task SubmitVoteAsync_WithCancellation_ShouldAcceptToken()
    {
        _engine = new VoteEngine(k: 2);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Note: Without embedding generator, cancellation doesn't affect much
        // This test just ensures the API accepts the token
        var result = await _engine.SubmitVoteAsync("A", cts.Token);
        result.ShouldBeNull(); // K=2 needs 2 votes
    }
}
