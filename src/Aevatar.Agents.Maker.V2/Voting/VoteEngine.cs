using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Aevatar.Agents.Maker.V2;

// ============================================================
//  Vote Engine - Core MAKER Consensus Mechanism
//  Implements First-to-ahead-by-K with N = 2K - 1
// ============================================================

/// <summary>
/// Voting engine implementing the first-to-ahead-by-K consensus algorithm.
/// </summary>
public sealed class VoteEngine
{
    private readonly int _k;
    private readonly int _maxRounds;
    private readonly ConcurrentDictionary<string, VoteCluster> _clusters = new();
    private int _totalVotes;
    private int _currentRound = 1;

    /// <summary>
    /// Creates a new vote engine.
    /// </summary>
    /// <param name="k">The K value for first-to-ahead-by-K.</param>
    /// <param name="maxRounds">Maximum voting rounds before failure.</param>
    public VoteEngine(int k, int maxRounds = 3)
    {
        _k = Math.Max(1, k);
        _maxRounds = Math.Max(1, maxRounds);
    }

    /// <summary>
    /// Number of samples needed per round. N = 2K - 1.
    /// </summary>
    public int SamplesPerRound => 2 * _k - 1;

    /// <summary>
    /// Current voting round.
    /// </summary>
    public int CurrentRound => _currentRound;

    /// <summary>
    /// Total votes received.
    /// </summary>
    public int TotalVotes => _totalVotes;

    /// <summary>
    /// Submit a proposal and check for consensus.
    /// </summary>
    /// <param name="content">The proposal content.</param>
    /// <returns>Consensus result if reached, null otherwise.</returns>
    public VoteResult? SubmitVote(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var canonical = Canonicalize(content);
        var hash = ComputeHash(canonical);

        _clusters.AddOrUpdate(
            hash,
            _ => new VoteCluster(hash, canonical, 1),
            (_, existing) => existing with { Votes = existing.Votes + 1 });

        Interlocked.Increment(ref _totalVotes);

        return CheckConsensus();
    }

    /// <summary>
    /// Check if consensus has been reached.
    /// </summary>
    public VoteResult? CheckConsensus()
    {
        if (_clusters.IsEmpty)
        {
            return null;
        }

        var ordered = _clusters.Values
            .OrderByDescending(c => c.Votes)
            .ToList();

        var leader = ordered[0];
        var runnerUpVotes = ordered.Count > 1 ? ordered[1].Votes : 0;

        // First-to-ahead-by-K rule
        if (leader.Votes - runnerUpVotes >= _k)
        {
            return new VoteResult
            {
                Success = true,
                WinningContent = leader.Content,
                WinningHash = leader.Hash,
                LeaderVotes = leader.Votes,
                RunnerUpVotes = runnerUpVotes,
                TotalVotes = _totalVotes,
                Rounds = _currentRound,
                AllCandidates = ordered.Select(c => new VoteCandidate
                {
                    Hash = c.Hash,
                    Content = c.Content,
                    Votes = c.Votes
                }).ToList()
            };
        }

        // Check if we need a new round
        var votesThisRound = _totalVotes - (_currentRound - 1) * SamplesPerRound;
        if (votesThisRound >= SamplesPerRound)
        {
            _currentRound++;
            if (_currentRound > _maxRounds)
            {
                // Max rounds exceeded - return best effort or failure
                return new VoteResult
                {
                    Success = false,
                    WinningContent = leader.Content,
                    WinningHash = leader.Hash,
                    LeaderVotes = leader.Votes,
                    RunnerUpVotes = runnerUpVotes,
                    TotalVotes = _totalVotes,
                    Rounds = _currentRound - 1,
                    FailureReason = "Max voting rounds exceeded without consensus",
                    AllCandidates = ordered.Select(c => new VoteCandidate
                    {
                        Hash = c.Hash,
                        Content = c.Content,
                        Votes = c.Votes
                    }).ToList()
                };
            }
        }

        return null;
    }

    /// <summary>
    /// Get current voting state for progress reporting.
    /// </summary>
    public VotingProgress GetProgress(VotingType type)
    {
        var ordered = _clusters.Values
            .OrderByDescending(c => c.Votes)
            .ToList();

        return new VotingProgress
        {
            Type = type,
            Round = _currentRound,
            TotalVotes = _totalVotes,
            VotesNeeded = _k,
            LeaderVotes = ordered.Count > 0 ? ordered[0].Votes : 0,
            RunnerUpVotes = ordered.Count > 1 ? ordered[1].Votes : 0
        };
    }

    /// <summary>
    /// Reset the engine for a new voting session.
    /// </summary>
    public void Reset()
    {
        _clusters.Clear();
        _totalVotes = 0;
        _currentRound = 1;
    }

    private static string Canonicalize(string content)
    {
        // Normalize whitespace and trim
        return content.Trim();
    }

    private static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes);
    }
}

/// <summary>
/// Internal vote cluster representation.
/// </summary>
internal sealed record VoteCluster(string Hash, string Content, int Votes);

/// <summary>
/// Result of a voting session.
/// </summary>
public sealed record VoteResult
{
    /// <summary>
    /// Whether consensus was reached.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// The winning content.
    /// </summary>
    public required string WinningContent { get; init; }

    /// <summary>
    /// Hash of the winning content.
    /// </summary>
    public required string WinningHash { get; init; }

    /// <summary>
    /// Leader vote count.
    /// </summary>
    public int LeaderVotes { get; init; }

    /// <summary>
    /// Runner-up vote count.
    /// </summary>
    public int RunnerUpVotes { get; init; }

    /// <summary>
    /// Total votes cast.
    /// </summary>
    public int TotalVotes { get; init; }

    /// <summary>
    /// Number of voting rounds.
    /// </summary>
    public int Rounds { get; init; }

    /// <summary>
    /// Failure reason if not successful.
    /// </summary>
    public string? FailureReason { get; init; }

    /// <summary>
    /// All candidates with their votes.
    /// </summary>
    public IReadOnlyList<VoteCandidate> AllCandidates { get; init; } = [];
}

