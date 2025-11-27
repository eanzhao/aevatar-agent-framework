using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;

namespace Aevatar.Agents.Maker;

// ============================================================
//  Vote Engine - Core MAKER Consensus Mechanism
//  Implements First-to-ahead-by-K with Semantic Clustering
//  
//  Optimizations:
//  - ReaderWriterLockSlim for read/write separation
//  - IO operations (embedding) outside lock
//  - SIMD-accelerated cosine similarity
//  - CPU-intensive work outside critical section
// ============================================================

/// <summary>
/// Voting engine implementing the first-to-ahead-by-K consensus algorithm
/// with semantic similarity clustering using embeddings.
/// </summary>
public sealed class VoteEngine : IDisposable
{
    private readonly int _k;
    private readonly int _maxRounds;
    private readonly float _similarityThreshold;
    private readonly IEmbeddingGenerator<string, Embedding<float>>? _embeddingGenerator;
    private readonly List<SemanticCluster> _clusters = [];
    private readonly ReaderWriterLockSlim _rwLock = new(LockRecursionPolicy.NoRecursion);
    private int _totalVotes;
    private int _currentRound = 1;
    private bool _disposed;

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
    public int CurrentRound
    {
        get
        {
            _rwLock.EnterReadLock();
            try { return _currentRound; }
            finally { _rwLock.ExitReadLock(); }
        }
    }

    /// <summary>
    /// Total votes received.
    /// </summary>
    public int TotalVotes
    {
        get
        {
            _rwLock.EnterReadLock();
            try { return _totalVotes; }
            finally { _rwLock.ExitReadLock(); }
        }
    }

    /// <summary>
    /// Whether semantic clustering is enabled.
    /// </summary>
    public bool UseSemanticClustering => _embeddingGenerator != null;

    // ============================================================
    //  Write Operations (WriteLock)
    // ============================================================

    /// <summary>
    /// Submit a proposal and check for consensus (async version for semantic clustering).
    /// </summary>
    public async Task<VoteResult?> SubmitVoteAsync(string content, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var canonical = Canonicalize(content);

        if (_embeddingGenerator != null)
        {
            // IO operation OUTSIDE lock - key optimization
            float[]? newEmbedding = null;
            try
            {
                var embeddings = await _embeddingGenerator.GenerateAsync([content], cancellationToken: ct);
                newEmbedding = embeddings.FirstOrDefault()?.Vector.ToArray();
            }
            catch
            {
                // Fallback to exact matching on embedding failure
            }

            if (newEmbedding is { Length: > 0 })
            {
                await AddVoteSemanticAsync(canonical, newEmbedding);
            }
            else
            {
                AddVoteExact(canonical);
            }
        }
        else
        {
            AddVoteExact(canonical);
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

        if (_embeddingGenerator != null)
        {
            // For sync call, we need to block on embedding generation
            // This is not ideal but maintains compatibility
            float[]? newEmbedding = null;
            try
            {
                var embeddings = _embeddingGenerator.GenerateAsync([content]).GetAwaiter().GetResult();
                newEmbedding = embeddings.FirstOrDefault()?.Vector.ToArray();
            }
            catch
            {
                // Fallback to exact matching
            }

            if (newEmbedding is { Length: > 0 })
            {
                AddVoteSemanticAsync(canonical, newEmbedding).GetAwaiter().GetResult();
            }
            else
            {
                AddVoteExact(canonical);
            }
        }
        else
        {
            AddVoteExact(canonical);
        }

        return CheckConsensus();
    }

    /// <summary>
    /// Add vote using exact hash matching.
    /// </summary>
    private void AddVoteExact(string content)
    {
        var hash = ComputeHash(content);

        _rwLock.EnterWriteLock();
        try
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
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Add vote using semantic similarity clustering.
    /// </summary>
    private Task AddVoteSemanticAsync(string content, float[] newEmbedding)
    {
        // Step 1: Take a snapshot of existing clusters (ReadLock)
        List<(SemanticCluster Cluster, float[] Embedding)> snapshot;

        _rwLock.EnterReadLock();
        try
        {
            snapshot = _clusters
                .Where(c => c.Embedding != null)
                .Select(c => (c, c.Embedding!))
                .ToList();
        }
        finally
        {
            _rwLock.ExitReadLock();
        }

        // Step 2: Compute similarities OUTSIDE lock (CPU-intensive)
        SemanticCluster? bestMatch = null;
        float bestSimilarity = 0;

        foreach (var (cluster, embedding) in snapshot)
        {
            var similarity = CosineSimilaritySimd(newEmbedding, embedding);
            if (similarity > bestSimilarity && similarity >= _similarityThreshold)
            {
                bestSimilarity = similarity;
                bestMatch = cluster;
            }
        }

        // Step 3: Update state (WriteLock)
        _rwLock.EnterWriteLock();
        try
        {
            if (bestMatch != null)
            {
                // Re-verify the cluster still exists (rare case of concurrent modification)
                if (_clusters.Contains(bestMatch))
                {
                    bestMatch.Votes++;
                    bestMatch.Contents.Add(content);
                }
                else
                {
                    // Cluster was removed, create new one
                    CreateNewCluster(content, newEmbedding);
                }
            }
            else
            {
                CreateNewCluster(content, newEmbedding);
            }

            _totalVotes++;
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Create a new cluster (must be called within WriteLock).
    /// </summary>
    private void CreateNewCluster(string content, float[]? embedding)
    {
        _clusters.Add(new SemanticCluster
        {
            Hash = ComputeHash(content),
            RepresentativeContent = content,
            Embedding = embedding,
            Votes = 1,
            Contents = [content]
        });
    }

    /// <summary>
    /// Reset the engine for a new voting session.
    /// </summary>
    public void Reset()
    {
        _rwLock.EnterWriteLock();
        try
        {
            _clusters.Clear();
            _totalVotes = 0;
            _currentRound = 1;
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    // ============================================================
    //  Read Operations (ReadLock)
    // ============================================================

    /// <summary>
    /// Check if consensus has been reached.
    /// </summary>
    public VoteResult? CheckConsensus()
    {
        _rwLock.EnterUpgradeableReadLock();
        try
        {
            if (_clusters.Count == 0)
                return null;

            var ordered = _clusters.OrderByDescending(c => c.Votes).ToList();
            var leader = ordered[0];
            var runnerUpVotes = ordered.Count > 1 ? ordered[1].Votes : 0;

            // First-to-ahead-by-K rule
            if (leader.Votes - runnerUpVotes >= _k)
            {
                return BuildVoteResult(true, leader, runnerUpVotes, ordered, null);
            }

            // Check if we need a new round
            var votesThisRound = _totalVotes - (_currentRound - 1) * SamplesPerRound;
            if (votesThisRound >= SamplesPerRound)
            {
                // Upgrade to write lock for round increment
                _rwLock.EnterWriteLock();
                try
                {
                    _currentRound++;
                    if (_currentRound > _maxRounds)
                    {
                        return BuildVoteResult(false, leader, runnerUpVotes, ordered,
                            "Max voting rounds exceeded without consensus");
                    }
                }
                finally
                {
                    _rwLock.ExitWriteLock();
                }
            }

            return null;
        }
        finally
        {
            _rwLock.ExitUpgradeableReadLock();
        }
    }

    /// <summary>
    /// Get current voting state for progress reporting.
    /// </summary>
    public VotingProgress GetProgress(VotingType type)
    {
        _rwLock.EnterReadLock();
        try
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
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Get the best candidate (highest vote count) even if no consensus reached.
    /// Used for fallback when decomposition is not possible.
    /// </summary>
    public VoteCandidate? GetBestCandidate()
    {
        _rwLock.EnterReadLock();
        try
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
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Get all candidates ordered by vote count (descending).
    /// </summary>
    public List<VoteCandidate> GetAllCandidates()
    {
        _rwLock.EnterReadLock();
        try
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
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    // ============================================================
    //  Helper Methods
    // ============================================================

    private VoteResult BuildVoteResult(
        bool success,
        SemanticCluster leader,
        int runnerUpVotes,
        List<SemanticCluster> ordered,
        string? failureReason)
    {
        return new VoteResult
        {
            Success = success,
            WinningContent = leader.RepresentativeContent,
            WinningHash = leader.Hash,
            LeaderVotes = leader.Votes,
            RunnerUpVotes = runnerUpVotes,
            TotalVotes = _totalVotes,
            Rounds = _currentRound,
            ClusterCount = _clusters.Count,
            UsedSemanticClustering = _embeddingGenerator != null,
            FailureReason = failureReason,
            AllCandidates = ordered.Select(c => new VoteCandidate
            {
                Hash = c.Hash,
                Content = c.RepresentativeContent,
                Votes = c.Votes,
                ClusterSize = c.Contents.Count
            }).ToList()
        };
    }

    private static string Canonicalize(string content) => content.Trim();

    private static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes)[..16];
    }

    /// <summary>
    /// SIMD-accelerated cosine similarity calculation.
    /// Falls back to scalar implementation for remainder elements.
    /// </summary>
    private static float CosineSimilaritySimd(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;
        if (a.Length == 0) return 0;

        var length = a.Length;
        float dotProduct = 0, normA = 0, normB = 0;

        // SIMD path
        var simdLength = Vector<float>.Count;
        var simdEnd = length - (length % simdLength);

        for (var i = 0; i < simdEnd; i += simdLength)
        {
            var va = new Vector<float>(a, i);
            var vb = new Vector<float>(b, i);
            dotProduct += Vector.Dot(va, vb);
            normA += Vector.Dot(va, va);
            normB += Vector.Dot(vb, vb);
        }

        // Scalar path for remainder
        for (var i = simdEnd; i < length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denominator == 0 ? 0 : dotProduct / denominator;
    }

    // ============================================================
    //  IDisposable
    // ============================================================

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _rwLock.Dispose();
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
