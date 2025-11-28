using System.Text.Json;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Core.Utils;
using Aevatar.Agents.Maker.Messages;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Maker.Agents;

// ============================================================
//  MAKER Voting Coordinator
//  Handles streaming race voting with worker agents
//  Part of MakerCoordinatorGAgent (partial class)
// ============================================================

public partial class MakerCoordinatorGAgent
{
    // ============================================================
    //  Streaming Race Voting
    //  Implements MAKER Paper's Algorithm 4: First-to-ahead-by-K
    // ============================================================

    /// <summary>
    /// Run streaming race voting with worker agents.
    /// 
    /// MAKER Paper Design (Algorithm 4 - First-to-ahead-by-K):
    /// - Dispatch N workers in parallel
    /// - As each result arrives, IMMEDIATELY submit vote (no waiting)
    /// - If consensus reached, CANCEL remaining workers (early termination)
    /// - If first batch fails, dispatch more workers (continuous sampling)
    /// - Maximum samples = 3*N to prevent infinite loops
    /// 
    /// Key optimizations:
    /// - Latency = fastest K workers, not slowest worker (Straggler-resistant)
    /// - Token efficiency via early termination
    /// </summary>
    private async Task<(string? Result, string? BestCandidate, VotingSession? Session)> RunVotingWithWorkersAsync(
        string taskId,
        string prompt,
        bool isSolution,
        MakerOptions options,
        int depth,
        CancellationToken ct)
    {
        // Get embedding generator from AIGAgentBase
        TryGetEmbeddingGenerator(out var embeddingGenerator);

        // Create vote engine - supports continuous sampling
        var engine = new VoteEngine(
            options.ConsensusK,
            embeddingGenerator,
            maxRounds: 10,
            options.SemanticSimilarityThreshold);

        // Store for streaming race
        _currentVoteEngine = engine;
        _isSolutionVoting = isSolution;
        _currentDepth = depth;

        var systemPrompt = isSolution
            ? "You are a precise problem solver. Provide clear, direct answers."
            : "You are a precise task decomposition agent. Output ONLY valid JSON.";

        // Token limits from options (configurable per task type)
        var maxTokens = isSolution ? options.MaxSolutionTokens : options.MaxDecompositionTokens;
        var baseTemperature = isSolution ? 0.2f : 0.3f;

        // Maximum samples = 3 * N (prevent infinite loops)
        var maxTotalSamples = 3 * options.SamplesPerRound;
        var totalSamplesSent = 0;
        var round = 1;

        // Report voting start
        ReportProgress(new MakerProgress
        {
            Phase = MakerPhase.Voting,
            TaskId = taskId,
            Message = $"Starting streaming race with {_expectedWorkers} workers (K={options.ConsensusK})",
            Depth = depth,
            Voting = new VotingProgress
            {
                Type = isSolution ? VotingType.Solution : VotingType.Decomposition,
                Round = round,
                TotalVotes = 0,
                VotesNeeded = options.ConsensusK,
                LeaderVotes = 0,
                RunnerUpVotes = 0,
                ClusterCount = 0,
                UsedSemanticClustering = embeddingGenerator != null
            }
        });

        // Generate unique request prefix
        var requestPrefix = $"{taskId}:R{round}:";
        _currentVotingRequestPrefix = requestPrefix;
        _collectedProposals.Clear();
        CustomState.PendingProposals = 0;
        
        var startMsg = $">>> [VOTING-START] Prefix='{requestPrefix}' for task={taskId}, round={round}, isSolution={isSolution}";
        Logger.LogWarning(startMsg);  // Use Warning level for visibility in Aspire
        
        // ============================================================
        //  CRITICAL: Notify workers of new active prefix BEFORE dispatching requests
        //  Workers will skip any stale requests that don't match this prefix
        // ============================================================
        Logger.LogWarning(">>> [PREFIX-SEND] Broadcasting UpdateActivePrefix='{Prefix}' to workers", requestPrefix);
        await PublishAsync(new UpdateActivePrefix
        {
            CoordinatorId = Id.ToString(),
            ActivePrefix = requestPrefix
        }, EventDirection.Down);
        
        // ============================================================
        //  OPTIMIZATION: Brief delay to allow workers to process prefix update
        //  This helps workers skip stale requests before starting new LLM calls
        // ============================================================
        await Task.Delay(100, ct);  // 100ms to propagate prefix update

        // Setup consensus completion source for early termination
        _consensusCompletionSource = new TaskCompletionSource<VoteResult>();
        _votingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        VoteResult? voteResult = null;

        // Continuous sampling loop
        while (totalSamplesSent < maxTotalSamples && voteResult == null)
        {
            var batchSize = Math.Min(_expectedWorkers, maxTotalSamples - totalSamplesSent);
            _activeWorkerRequests = batchSize;

            Logger.LogInformation(
                "Dispatching batch {Round}: {BatchSize} workers (total sent: {Total}/{Max})",
                round, batchSize, totalSamplesSent + batchSize, maxTotalSamples);

            // Dispatch batch of workers
            for (var i = 0; i < batchSize; i++)
            {
                var requestId = $"{requestPrefix}W{totalSamplesSent + i}";
                var workerTemp = DecorrelateTemperature(baseTemperature, totalSamplesSent + i, options.TemperatureVariance);

                await PublishAsync(new GenerateProposalRequest
                {
                    RequestId = requestId,
                    TaskId = taskId,
                    SystemPrompt = systemPrompt,
                    UserPrompt = prompt,
                    Temperature = workerTemp,
                    MaxTokens = maxTokens,
                    IsDecomposition = !isSolution
                }, EventDirection.Down, ct);
            }

            totalSamplesSent += batchSize;

            // Wait for either:
            // 1. Consensus reached (early termination) - FAST PATH
            // 2. Timeout (per batch) - extended for large documents
            // 3. Cancellation
            try
            {
                using var batchCts = CancellationTokenSource.CreateLinkedTokenSource(_votingCts.Token);
                // Increased timeout: LLM processing large papers (50K+ tokens) can take 2+ minutes
                batchCts.CancelAfter(TimeSpan.FromSeconds(120));

                voteResult = await _consensusCompletionSource.Task.WaitAsync(batchCts.Token);

                Logger.LogInformation(
                    "EARLY TERMINATION: Consensus reached in {Ms}ms after {Samples} samples",
                    _stopwatch.ElapsedMilliseconds,
                    _collectedProposals.Count);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Batch timeout - give late proposals a grace period before moving on
                // This is critical for large documents where LLM response time varies significantly
                var proposalsBeforeGrace = _collectedProposals.Count;
                
                Logger.LogInformation(
                    "Batch {Round} timeout with {Proposals} proposals. Waiting grace period for late arrivals...",
                    round, proposalsBeforeGrace);
                
                // Grace period: wait up to 10 more seconds for any in-flight proposals
                // Keep the same prefix during grace period so late proposals are still accepted
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                
                var proposalsAfterGrace = _collectedProposals.Count;
                if (proposalsAfterGrace > proposalsBeforeGrace)
                {
                    Logger.LogInformation(
                        "Grace period captured {Count} additional proposals (total: {Total})",
                        proposalsAfterGrace - proposalsBeforeGrace, proposalsAfterGrace);
                }
                
                // Now check consensus with any late arrivals included
                voteResult = engine.CheckConsensus();

                if (voteResult == null && totalSamplesSent < maxTotalSamples)
                {
                    // No consensus yet, prepare next batch
                    round++;
                    requestPrefix = $"{taskId}:R{round}:";
                    _currentVotingRequestPrefix = requestPrefix;
                    _consensusCompletionSource = new TaskCompletionSource<VoteResult>();
                    
                    var nextMsg = $">>> [VOTING-NEXT-ROUND] Prefix='{requestPrefix}' for round={round}";
                    Logger.LogWarning(nextMsg);  // Use Warning level for visibility
                    
                    // Update workers with new active prefix
                    await PublishAsync(new UpdateActivePrefix
                    {
                        CoordinatorId = Id.ToString(),
                        ActivePrefix = requestPrefix
                    }, EventDirection.Down);

                    var progress = engine.GetProgress(isSolution ? VotingType.Solution : VotingType.Decomposition);

                    ReportProgress(new MakerProgress
                    {
                        Phase = MakerPhase.Voting,
                        TaskId = taskId,
                        Message = $"No consensus in batch {round - 1} ({_collectedProposals.Count} proposals), dispatching more workers",
                        Depth = depth,
                        Voting = progress
                    });
                }
            }
        }

        // Cleanup
        var endMsg = $">>> [VOTING-END] Clearing prefix (was '{_currentVotingRequestPrefix}'), collected {_collectedProposals.Count} proposals";
        Logger.LogWarning(endMsg);  // Use Warning level for visibility
        _currentVotingRequestPrefix = null;
        _currentVoteEngine = null;
        _votingCts?.Dispose();
        _votingCts = null;

        // Final consensus check
        voteResult ??= engine.CheckConsensus();

        // Get best candidate even if no consensus (for fallback)
        var bestCandidate = engine.GetBestCandidate();

        // Build session info
        var allCandidates = engine.GetAllCandidates();
        var candidates = allCandidates.Select(c => new VoteCandidate
        {
            Hash = c.Hash,
            Content = c.Content,
            Votes = c.Votes,
            ClusterSize = c.ClusterSize
        }).ToList();

        var session = new VotingSession
        {
            Type = isSolution ? VotingType.Solution : VotingType.Decomposition,
            Rounds = 1,
            Candidates = candidates,
            Winner = voteResult?.Success == true
                ? new VoteCandidate
                {
                    Hash = voteResult.WinningHash,
                    Content = voteResult.WinningContent,
                    Votes = voteResult.LeaderVotes
                }
                : null
        };

        // ============================================================
        // 📊 Final Voting Summary with Provider Distribution
        // ============================================================
        var votingType = isSolution ? "SOLUTION" : "DECOMPOSITION";
        var topCandidates = candidates.OrderByDescending(c => c.Votes).Take(5).ToList();
        var votingSummary = string.Join("\n", topCandidates.Select((c, i) => 
            $"    {i + 1}. {c.Votes} votes (cluster size: {c.ClusterSize}) - {(c.Content?.Length > 80 ? c.Content[..80].Replace("\n", " ") + "..." : c.Content?.Replace("\n", " "))}"));
        
        // Provider distribution
        var providerCounts = _collectedProposals
            .Where(p => !string.IsNullOrEmpty(p.ProviderName))
            .GroupBy(p => p.ProviderName)
            .ToDictionary(g => g.Key!, g => g.Count());
        var providerDistribution = providerCounts.Count > 0
            ? string.Join(", ", providerCounts.Select(kv => $"{kv.Key}: {kv.Value}"))
            : "N/A";
        
        Logger.LogInformation(
            "[PROVIDER-DISTRIBUTION] Collected {TotalProposals} proposals from: {Distribution}",
            _collectedProposals.Count, providerDistribution);
        
        if (voteResult?.Success != true)
        {
            // Per MAKER paper: No consensus means task is too complex
            Logger.LogWarning(
                "\n╔══════════════════════════════════════════════════════════════╗\n" +
                "║ ❌ NO CONSENSUS ({VotingType})                                 \n" +
                "╠══════════════════════════════════════════════════════════════╣\n" +
                "║ Task: {TaskId}                                                \n" +
                "║ Votes needed (K): {VotesNeeded}                               \n" +
                "║ Best candidate votes: {BestVotes}                             \n" +
                "║ Total clusters: {ClusterCount}                                \n" +
                "║ Top candidates:                                               \n" +
                "{TopCandidates}\n" +
                "║ → Task may need finer decomposition                           \n" +
                "╚══════════════════════════════════════════════════════════════╝",
                votingType,
                taskId,
                options.ConsensusK,
                bestCandidate?.Votes ?? 0,
                candidates.Count,
                votingSummary);
        }
        else
        {
            var winnerPreview = voteResult.WinningContent?.Length > 200
                ? voteResult.WinningContent[..200].Replace("\n", " ") + "..."
                : voteResult.WinningContent?.Replace("\n", " ");
            
            Logger.LogInformation(
                "\n╔══════════════════════════════════════════════════════════════╗\n" +
                "║ ✅ CONSENSUS ACHIEVED ({VotingType})                          \n" +
                "╠══════════════════════════════════════════════════════════════╣\n" +
                "║ Task: {TaskId}                                                \n" +
                "║ Winner: {LeaderVotes}/{VotesNeeded} votes                     \n" +
                "║ Total clusters: {ClusterCount}                                \n" +
                "║ Winning content:                                              \n" +
                "║   {WinnerPreview}                                             \n" +
                "╚══════════════════════════════════════════════════════════════╝",
                votingType,
                taskId,
                voteResult.LeaderVotes,
                options.ConsensusK,
                candidates.Count,
                winnerPreview);
            
            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Voting,
                TaskId = taskId,
                Message = $"✓ Voting complete: consensus achieved with {voteResult.LeaderVotes} votes",
                Depth = depth
            });
        }

        return (
            voteResult?.Success == true ? voteResult.WinningContent : null,
            bestCandidate?.Content,
            session);
    }

    // ============================================================
    //  Helper Methods
    // ============================================================

    /// <summary>
    /// Parse atomicity assessment JSON response.
    /// Returns (isAtomic, reason).
    /// Uses LLMResponseParser for robust JSON extraction.
    /// </summary>
    private static (bool IsAtomic, string Reason) ParseAtomicityResponse(string content)
    {
        // Use LLMResponseParser for robust JSON extraction
        var doc = LLMResponseParser.ParseToDocument(content);
        if (doc != null)
        {
            try
            {
                var root = doc.RootElement;
                var isAtomic = root.TryGetProperty("atomic", out var atomicProp) && atomicProp.GetBoolean();
                var reason = root.TryGetProperty("reason", out var reasonProp)
                    ? reasonProp.GetString() ?? "no reason provided"
                    : "no reason provided";

                return (isAtomic, reason);
            }
            finally
            {
                doc.Dispose();
            }
        }

        // Fallback: check for keywords in raw content
        var upper = content.ToUpperInvariant();
        if (upper.Contains("\"ATOMIC\"") || upper.Contains(":TRUE") || upper.Contains(": TRUE"))
        {
            return (true, "parsed from keywords");
        }

        return (false, "failed to parse JSON, defaulting to decompose");
    }

    /// <summary>
    /// Decorrelate temperature for worker diversification.
    /// </summary>
    private static float DecorrelateTemperature(float baseTemp, int index, float variance)
    {
        var offset = (variance * index) - (variance * 0.5f);
        return Math.Clamp(baseTemp + offset, 0.0f, 2.0f);
    }
}

