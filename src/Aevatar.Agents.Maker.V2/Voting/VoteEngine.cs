using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;

namespace Aevatar.Agents.Maker.V2;

// ============================================================
//  Vote Engine - Core MAKER Consensus Mechanism
//  Implements First-to-ahead-by-K with Semantic Clustering
// ============================================================

/// <summary>
/// Voting engine implementing the first-to-ahead-by-K consensus algorithm
/// with semantic similarity clustering using embeddings.
/// </summary>
public sealed class VoteEngine
{
    private readonly int _k;
    private readonly int _maxRounds;
    private readonly float _similarityThreshold;
    private readonly IEmbeddingGenerator<string, Embedding<float>>? _embeddingGenerator;
    private readonly List<SemanticCluster> _clusters = [];
    private readonly object _lock = new();
    private int _totalVotes;
    private int _currentRound = 1;

    /// <summary>
    /// Creates a new vote engine with semantic clustering.
    /// </summary>
    /// <param name="k">The K value for first-to-ahead-by-K.</param>
    /// <param name="embeddingGenerator">Embedding generator for semantic similarity. If null, falls back to exact matching.</param>
    /// <param name="maxRounds">Maximum voting rounds before failure.</param>
    /// <param name="similarityThreshold">Semantic similarity threshold (0.0-1.0). Default: 0.85</param>
    public VoteEngine(
        int k, 
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null,
        int maxRounds = 3, 
        float similarityThreshold = 0.85f)
    {
        _k = Math.Max(1, k);
        _maxRounds = Math.Max(1, maxRounds);
        _similarityThreshold = Math.Clamp(similarityThreshold, 0.1f, 1.0f);
        _embeddingGenerator = embeddingGenerator;
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
    /// Whether semantic clustering is enabled.
    /// </summary>
    public bool UseSemanticClustering => _embeddingGenerator != null;

    /// <summary>
    /// Submit a proposal and check for consensus (async version for semantic clustering).
    /// </summary>
    public async Task<VoteResult?> SubmitVoteAsync(string content, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var canonical = Canonicalize(content);

        lock (_lock)
        {
            if (_embeddingGenerator == null)
            {
                // Fallback to exact hash matching
                AddVoteExact(canonical);
            }
            else
            {
                // Use embedding-based semantic clustering (async part handled outside lock)
            }
        }

        if (_embeddingGenerator != null)
        {
            await AddVoteSemanticAsync(canonical, ct);
        }

        return CheckConsensus();
    }

    /// <summary>
    /// Submit a proposal (synchronous, uses exact matching or cached embeddings).
    /// </summary>
    public VoteResult? SubmitVote(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var canonical = Canonicalize(content);

        lock (_lock)
        {
            if (_embeddingGenerator == null)
            {
                AddVoteExact(canonical);
            }
            else
            {
                // For sync call with embedding, do blocking call (not ideal but works)
                AddVoteSemanticAsync(canonical, CancellationToken.None).GetAwaiter().GetResult();
            }
        }

        return CheckConsensus();
    }

    private void AddVoteExact(string content)
    {
        var hash = ComputeHash(content);
        
        lock (_lock)
        {
            var existing = _clusters.FirstOrDefault(c => c.Hash == hash);
            if (existing != null)
            {
                existing.Votes++;
                existing.Contents.Add(content);
            }
            else
            {
                _clusters.Add(new SemanticCluster
                {
                    Hash = hash,
                    RepresentativeContent = content,
                    Embedding = null,
                    Votes = 1,
                    Contents = [content]
                });
            }
            _totalVotes++;
        }
    }

    private async Task AddVoteSemanticAsync(string content, CancellationToken ct)
    {
        // Generate embedding for the new content
        var embeddings = await _embeddingGenerator!.GenerateAsync([content], cancellationToken: ct);
        var newEmbedding = embeddings.FirstOrDefault()?.Vector.ToArray();
        
        if (newEmbedding == null || newEmbedding.Length == 0)
        {
            // Fallback to exact matching if embedding fails
            AddVoteExact(content);
            return;
        }

        lock (_lock)
        {
            // Find the most similar cluster
            SemanticCluster? bestMatch = null;
            float bestSimilarity = 0;

            foreach (var cluster in _clusters)
            {
                if (cluster.Embedding == null) continue;
                
                var similarity = CosineSimilarity(newEmbedding, cluster.Embedding);
                if (similarity > bestSimilarity && similarity >= _similarityThreshold)
                {
                    bestSimilarity = similarity;
                    bestMatch = cluster;
                }
            }

            if (bestMatch != null)
            {
                // Add to existing cluster
                bestMatch.Votes++;
                bestMatch.Contents.Add(content);
            }
            else
            {
                // Create new cluster
                _clusters.Add(new SemanticCluster
                {
                    Hash = ComputeHash(content),
                    RepresentativeContent = content,
                    Embedding = newEmbedding,
                    Votes = 1,
                    Contents = [content]
                });
            }
            _totalVotes++;
        }
    }

    /// <summary>
    /// Check if consensus has been reached.
    /// </summary>
    public VoteResult? CheckConsensus()
    {
        lock (_lock)
        {
            if (_clusters.Count == 0)
                return null;

            var ordered = _clusters.OrderByDescending(c => c.Votes).ToList();
            var leader = ordered[0];
            var runnerUpVotes = ordered.Count > 1 ? ordered[1].Votes : 0;

            // First-to-ahead-by-K rule
            if (leader.Votes - runnerUpVotes >= _k)
            {
                return new VoteResult
                {
                    Success = true,
                    WinningContent = leader.RepresentativeContent,
                    WinningHash = leader.Hash,
                    LeaderVotes = leader.Votes,
                    RunnerUpVotes = runnerUpVotes,
                    TotalVotes = _totalVotes,
                    Rounds = _currentRound,
                    ClusterCount = _clusters.Count,
                    UsedSemanticClustering = _embeddingGenerator != null,
                    AllCandidates = ordered.Select(c => new VoteCandidate
                    {
                        Hash = c.Hash,
                        Content = c.RepresentativeContent,
                        Votes = c.Votes,
                        ClusterSize = c.Contents.Count
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
                    return new VoteResult
                    {
                        Success = false,
                        WinningContent = leader.RepresentativeContent,
                        WinningHash = leader.Hash,
                        LeaderVotes = leader.Votes,
                        RunnerUpVotes = runnerUpVotes,
                        TotalVotes = _totalVotes,
                        Rounds = _currentRound - 1,
                        ClusterCount = _clusters.Count,
                        UsedSemanticClustering = _embeddingGenerator != null,
                        FailureReason = "Max voting rounds exceeded without consensus",
                        AllCandidates = ordered.Select(c => new VoteCandidate
                        {
                            Hash = c.Hash,
                            Content = c.RepresentativeContent,
                            Votes = c.Votes,
                            ClusterSize = c.Contents.Count
                        }).ToList()
                    };
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Get current voting state for progress reporting.
    /// </summary>
    public VotingProgress GetProgress(VotingType type)
    {
        lock (_lock)
        {
            var ordered = _clusters.OrderByDescending(c => c.Votes).ToList();

            return new VotingProgress
            {
                Type = type,
                Round = _currentRound,
                TotalVotes = _totalVotes,
                VotesNeeded = _k,
                LeaderVotes = ordered.Count > 0 ? ordered[0].Votes : 0,
                RunnerUpVotes = ordered.Count > 1 ? ordered[1].Votes : 0,
                ClusterCount = _clusters.Count,
                UsedSemanticClustering = _embeddingGenerator != null
            };
        }
    }

    /// <summary>
    /// Get the best candidate (highest vote count) even if no consensus reached.
    /// Used for fallback when decomposition is not possible.
    /// </summary>
    public VoteCandidate? GetBestCandidate()
    {
        lock (_lock)
        {
            if (_clusters.Count == 0)
                return null;

            var best = _clusters.MaxBy(c => c.Votes);
            if (best == null)
                return null;

            return new VoteCandidate
            {
                Hash = best.Hash,
                Content = best.RepresentativeContent,
                Votes = best.Votes,
                ClusterSize = best.Contents.Count
            };
        }
    }

    /// <summary>
    /// Get all candidates ordered by vote count (descending).
    /// </summary>
    public List<VoteCandidate> GetAllCandidates()
    {
        lock (_lock)
        {
            return _clusters
                .OrderByDescending(c => c.Votes)
                .Select(c => new VoteCandidate
                {
                    Hash = c.Hash,
                    Content = c.RepresentativeContent,
                    Votes = c.Votes,
                    ClusterSize = c.Contents.Count
                })
                .ToList();
        }
    }

    /// <summary>
    /// Reset the engine for a new voting session.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _clusters.Clear();
            _totalVotes = 0;
            _currentRound = 1;
        }
    }

    // ============================================================
    //  Helper Methods
    // ============================================================

    private static string Canonicalize(string content)
    {
        return content.Trim();
    }

    private static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes)[..16]; // Shorter hash for display
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;

        float dotProduct = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denominator == 0 ? 0 : dotProduct / denominator;
    }
}

/// <summary>
/// Internal semantic cluster representation.
/// </summary>
internal sealed class SemanticCluster
{
    public required string Hash { get; init; }
    public required string RepresentativeContent { get; set; }
    public float[]? Embedding { get; set; }
    public int Votes { get; set; }
    public List<string> Contents { get; init; } = [];
}

/// <summary>
/// Result of a voting session.
/// </summary>
public sealed record VoteResult
{
    public required bool Success { get; init; }
    public required string WinningContent { get; init; }
    public required string WinningHash { get; init; }
    public int LeaderVotes { get; init; }
    public int RunnerUpVotes { get; init; }
    public int TotalVotes { get; init; }
    public int Rounds { get; init; }
    public int ClusterCount { get; init; }
    public bool UsedSemanticClustering { get; init; }
    public string? FailureReason { get; init; }
    public IReadOnlyList<VoteCandidate> AllCandidates { get; init; } = [];
}

// VoteCandidate, VotingProgress, and VotingType are defined in MakerProgress.cs
