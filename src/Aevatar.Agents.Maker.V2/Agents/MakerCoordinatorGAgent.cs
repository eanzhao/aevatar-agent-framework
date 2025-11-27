using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Maker.V2.Messages;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Maker.V2.Agents;

// ============================================================
//  MAKER Coordinator Agent
//  Pure event-driven - ALL communication via Stream
// ============================================================

/// <summary>
/// MAKER Coordinator Agent - orchestrates the MAKER workflow.
/// All communication with workers happens through events (no direct method calls).
/// 
/// Event Flow:
/// 1. Receives StartMakerTaskRequest -> Creates workers -> Publishes InitializeWorkerRequest (DOWN)
/// 2. Receives WorkerInitialized (from workers)
/// 3. Publishes GenerateProposalRequest (DOWN) -> Workers process
/// 4. Receives ProposalResult (from workers) -> Runs voting
/// 5. Publishes MakerTaskCompleted when done
/// </summary>
public class MakerCoordinatorGAgent : AIGAgentBase<MakerCoordinatorState, MakerCoordinatorConfig>
{
    // Strategies
    private IDecompositionStrategy _decomposer = new DefaultDecomposer();
    private ISolutionStrategy _solver = new DefaultSolver();
    private ICompositionStrategy _composer = new DefaultComposer();
    
    // Runtime state (not persisted)
    private readonly Stopwatch _stopwatch = new();
    private readonly List<RedFlagEvent> _redFlags = [];
    private Action<MakerProgress>? _progressCallback;
    
    // Async coordination
    private TaskCompletionSource<bool>? _initCompletionSource;
    private TaskCompletionSource<List<ProposalResult>>? _votingCompletionSource;
    private readonly ConcurrentBag<ProposalResult> _collectedProposals = [];
    private int _workersInitialized;
    private int _expectedWorkers;
    private string? _currentVotingRequestPrefix;
    
    // Execution state
    private MakerResult? _cachedResult;
    private MakerOptions? _currentOptions;
    private Dictionary<string, string>? _currentContext;

    public MakerCoordinatorGAgent() { }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"MAKER Coordinator [{CustomState.ExecutionId}] - Workers: {CustomState.ActiveWorkers}");
    }

    #region Public API (for AgentMakerExecutor)

    /// <summary>
    /// Set external dependencies (called before sending StartMakerTaskRequest).
    /// This is the ONLY direct call allowed - to inject non-serializable dependencies.
    /// Note: Embedding generator is provided by AIGAgentBase via TryGetEmbeddingGenerator().
    /// </summary>
    public void SetDependencies(
        IDecompositionStrategy? decomposer = null,
        ISolutionStrategy? solver = null,
        ICompositionStrategy? composer = null,
        Action<MakerProgress>? progressCallback = null)
    {
        _decomposer = decomposer ?? new DefaultDecomposer();
        _solver = solver ?? new DefaultSolver();
        _composer = composer ?? new DefaultComposer();
        _progressCallback = progressCallback;
    }

    /// <summary>
    /// Get the execution result (called after checking status is completed).
    /// </summary>
    public MakerResult GetResult()
    {
        return _cachedResult ?? new MakerResult
        {
            Success = false,
            Content = string.Empty,
            Error = "Result not available",
            Trace = new MakerTrace
            {
                ExecutionId = CustomState.ExecutionId,
                RootTask = new TaskNode
                {
                    TaskId = CustomState.ExecutionId,
                    Description = CustomState.TaskDescription,
                    Depth = 0,
                    IsAtomic = false
                },
                TotalLLMCalls = CustomState.TotalLlmCalls,
                Duration = _stopwatch.Elapsed,
                RedFlags = _redFlags.ToList()
            }
        };
    }

    /// <summary>
    /// Get execution status (0=idle, 1=starting, 2=running, 3=completed, 4=failed, 5=cancelled).
    /// </summary>
    public int GetStatus() => CustomState.Status;

    /// <summary>
    /// Get total LLM calls made.
    /// </summary>
    public int GetTotalLlmCalls() => CustomState.TotalLlmCalls;

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handle start task request - entry point for execution.
    /// </summary>
    [EventHandler]
    public async Task HandleStartMakerTaskRequest(StartMakerTaskRequest request)
    {
        Logger.LogInformation("Coordinator {Id} received start request for task: {TaskDescription}",
            Id, request.TaskDescription[..Math.Min(50, request.TaskDescription.Length)]);
        
        try
        {
            // Initialize state
            CustomState.ExecutionId = request.ExecutionId;
            CustomState.TaskDescription = request.TaskDescription;
            CustomState.MaxDepth = request.MaxDepth;
            CustomState.ConsensusK = request.ConsensusK;
            CustomState.SamplesPerRound = request.SamplesPerRound;
            CustomState.Status = 1; // Starting
            CustomState.StartedAt = Timestamp.FromDateTime(DateTime.UtcNow);
            CustomState.TotalLlmCalls = 0;
            CustomState.RedFlagReasons.Clear();
            _redFlags.Clear();

            // Setup config
            CustomConfig.DefaultConsensusK = request.ConsensusK;
            CustomConfig.DefaultMaxDepth = request.MaxDepth;
            CustomConfig.BaseTemperature = request.BaseTemperature;
            CustomConfig.TemperatureVariance = request.TemperatureVariance;
            CustomConfig.SemanticSimilarityThreshold = request.SemanticSimilarityThreshold;
            CustomConfig.ClusteringMethod = request.ClusteringMethod;
            CustomConfig.LlmProviderName = request.ProviderName;
            CustomConfig.WorkerPoolSize = request.SamplesPerRound;

            // Store options for later use
            _currentOptions = new MakerOptions
            {
                CustomK = request.ConsensusK,
                MaxDepth = request.MaxDepth,
                BaseTemperature = request.BaseTemperature,
                TemperatureVariance = request.TemperatureVariance,
                SemanticSimilarityThreshold = request.SemanticSimilarityThreshold,
                ClusteringMethod = request.ClusteringMethod,
                ProviderName = request.ProviderName,
                Decomposer = _decomposer,
                Solver = _solver,
                Composer = _composer,
                OnProgress = _progressCallback
            };
            _currentContext = request.Context != null
                ? new Dictionary<string, string>(request.Context)
                : new Dictionary<string, string>();

            // Initialize AI for synthesis
            await InitializeAsync(request.ProviderName, config =>
            {
                config.Temperature = request.BaseTemperature;
                config.MaxOutputTokens = 4096;
            });

            _stopwatch.Restart();

            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Starting,
                TaskId = request.ExecutionId,
                Message = $"MAKER System Online - K={request.ConsensusK}, N={request.SamplesPerRound}, MaxDepth={request.MaxDepth}",
                Depth = 0
            });
            
            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Starting,
                TaskId = request.ExecutionId,
                Message = $"Initializing {request.SamplesPerRound} worker agents for parallel LLM execution",
                Depth = 0
            });

            // Initialize workers via events
            _expectedWorkers = request.SamplesPerRound;
            _workersInitialized = 0;
            _initCompletionSource = new TaskCompletionSource<bool>();

            // Send initialization requests to workers (DOWN to children)
            for (var i = 0; i < request.SamplesPerRound; i++)
            {
                var workerTemp = DecorrelateTemperature(request.BaseTemperature, i, request.TemperatureVariance);
                await PublishAsync(new InitializeWorkerRequest
                {
                    CoordinatorId = Id.ToString(),
                    ProviderName = request.ProviderName,
                    WorkerIndex = i,
                    Temperature = workerTemp
                }, EventDirection.Down);
            }

            // Wait for all workers to initialize (with timeout)
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await _initCompletionSource.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"Workers failed to initialize within timeout. Got {_workersInitialized}/{_expectedWorkers}");
            }

            Logger.LogInformation("All {Count} workers initialized", _expectedWorkers);

            // Start execution
            CustomState.Status = 2; // Running
            
            var (result, node) = await ExecuteTaskRecursiveAsync(
                taskId: request.ExecutionId,
                description: request.TaskDescription,
                depth: 0,
                context: _currentContext,
                ct: default);

            _stopwatch.Stop();

            var success = result != null;
            CustomState.Status = success ? 3 : 4;
            CustomState.UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);

            _cachedResult = new MakerResult
            {
                Success = success,
                Content = result ?? string.Empty,
                Error = success ? null : "Task execution failed",
                Trace = new MakerTrace
                {
                    ExecutionId = request.ExecutionId,
                    RootTask = node,
                    TotalLLMCalls = CustomState.TotalLlmCalls,
                    Duration = _stopwatch.Elapsed,
                    RedFlags = _redFlags.ToList()
                }
            };

            ReportProgress(new MakerProgress
            {
                Phase = success ? MakerPhase.Completed : MakerPhase.Failed,
                TaskId = request.ExecutionId,
                Message = success ? "Execution completed successfully" : "Execution failed",
                Depth = 0
            });

            // Publish completion event
            await PublishAsync(new MakerTaskCompleted
            {
                ExecutionId = request.ExecutionId,
                Success = success,
                Content = result ?? string.Empty,
                Error = success ? string.Empty : "Task execution failed",
                TotalLlmCalls = CustomState.TotalLlmCalls,
                DurationMs = (long)_stopwatch.Elapsed.TotalMilliseconds,
                TraceJson = JsonSerializer.Serialize(_cachedResult!.Trace)
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Coordinator {Id} failed during execution", Id);
            _stopwatch.Stop();
            CustomState.Status = 4;

            _cachedResult = new MakerResult
            {
                Success = false,
                Content = string.Empty,
                Error = ex.Message,
                Trace = new MakerTrace
                {
                    ExecutionId = request.ExecutionId,
                    RootTask = new TaskNode
                    {
                        TaskId = request.ExecutionId,
                        Description = request.TaskDescription,
                        Depth = 0,
                        IsAtomic = false
                    },
                    TotalLLMCalls = CustomState.TotalLlmCalls,
                    Duration = _stopwatch.Elapsed,
                    RedFlags = _redFlags.ToList()
                }
            };

            await PublishAsync(new MakerTaskCompleted
            {
                ExecutionId = request.ExecutionId,
                Success = false,
                Error = ex.Message,
                TotalLlmCalls = CustomState.TotalLlmCalls,
                DurationMs = (long)_stopwatch.Elapsed.TotalMilliseconds
            });
        }
    }

    /// <summary>
    /// Handle worker initialized event.
    /// </summary>
    [EventHandler]
    public Task HandleWorkerInitialized(WorkerInitialized evt)
    {
        Logger.LogDebug("Worker {WorkerId} initialized (index: {Index})", evt.WorkerId, evt.WorkerIndex);

        if (!CustomState.WorkerIds.Contains(evt.WorkerId))
        {
            CustomState.WorkerIds.Add(evt.WorkerId);
        }

        var count = Interlocked.Increment(ref _workersInitialized);
        CustomState.ActiveWorkers = count;

        if (count >= _expectedWorkers)
        {
            _initCompletionSource?.TrySetResult(true);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Handle proposal results from workers.
    /// </summary>
    [EventHandler]
    public Task HandleProposalResult(ProposalResult result)
    {
        Logger.LogDebug("Received proposal {ProposalId} from worker {WorkerId} for task {TaskId}",
            result.ProposalId, result.WorkerId, result.TaskId);

        // Check if this proposal belongs to current voting session
        if (_currentVotingRequestPrefix != null && result.RequestId.StartsWith(_currentVotingRequestPrefix))
        {
            _collectedProposals.Add(result);
            CustomState.PendingProposals = _collectedProposals.Count;

            // Check if we have all proposals
            if (_collectedProposals.Count >= _expectedWorkers)
            {
                _votingCompletionSource?.TrySetResult(_collectedProposals.ToList());
            }
        }

        return Task.CompletedTask;
    }

    #endregion

    #region Recursive Task Execution

    private async Task<(string? Result, TaskNode Node)> ExecuteTaskRecursiveAsync(
        string taskId,
        string description,
        int depth,
        Dictionary<string, string> context,
        CancellationToken ct)
    {
        CustomState.CurrentTaskId = taskId;
        CustomState.CurrentDepth = depth;

        ReportProgress(new MakerProgress
        {
            Phase = MakerPhase.Assessing,
            TaskId = taskId,
            Message = $"Assessing task complexity at depth {depth}",
            Depth = depth
        });

        var options = _currentOptions ?? new MakerOptions();
        var isAtomic = _decomposer.IsAtomic(description, depth, options.MaxDepth);

        if (isAtomic)
        {
            // MAKER Paper: If atomic task solution fails and we have depth budget,
            // try decomposition instead (task may be too complex)
            var (result, node) = await SolveAtomicTaskAsync(taskId, description, context, depth, ct);
            
            if (result != null)
            {
                return (result, node);
            }
            
            // Solution failed - per MAKER paper, this means task is too complex
            if (depth < options.MaxDepth)
            {
                Logger.LogInformation(
                    "Task {TaskId}: Solution voting failed at depth {Depth}, attempting decomposition (MAKER paper rule)",
                    taskId, depth);
                    
                AddRedFlag(taskId, $"Solution consensus failed at depth {depth}, trying decomposition");
                
                return await DecomposeAndExecuteAsync(taskId, description, context, depth, ct);
            }
            
            // At max depth with no consensus - use best candidate as fallback
            if (node.VotingSessions.Count > 0 && node.VotingSessions[0].Candidates.Count > 0)
            {
                var bestCandidate = node.VotingSessions[0].Candidates
                    .OrderByDescending(c => c.Votes)
                    .First();
                    
                AddRedFlag(taskId, $"At max depth without consensus, using best candidate (votes: {bestCandidate.Votes})");
                node.Result = bestCandidate.Content;
                return (bestCandidate.Content, node);
            }
            
            return (null, node);
        }

        return await DecomposeAndExecuteAsync(taskId, description, context, depth, ct);
    }

    private async Task<(string? Result, TaskNode Node)> SolveAtomicTaskAsync(
        string taskId,
        string description,
        Dictionary<string, string> context,
        int depth,
        CancellationToken ct)
    {
        ReportProgress(new MakerProgress
        {
            Phase = MakerPhase.Solving,
            TaskId = taskId,
            Message = "Solving atomic task with worker agents",
            Depth = depth
        });

        var prompt = _solver.BuildSolvePrompt(description, context);
        var options = _currentOptions ?? new MakerOptions();
        var (result, bestCandidate, session) = await RunVotingWithWorkersAsync(
            taskId, prompt, isSolution: true, options, depth, ct);

        var sessions = session != null ? new List<VotingSession> { session } : new List<VotingSession>();

        return (result, new TaskNode
        {
            TaskId = taskId,
            Description = description,
            Depth = depth,
            IsAtomic = true,
            Result = result,
            FallbackCandidate = bestCandidate,  // Store for potential fallback use
            VotingSessions = sessions
        });
    }

    private async Task<(string? Result, TaskNode Node)> DecomposeAndExecuteAsync(
        string taskId,
        string description,
        Dictionary<string, string> context,
        int depth,
        CancellationToken ct)
    {
        var sessions = new List<VotingSession>();
        var children = new List<TaskNode>();
        var options = _currentOptions ?? new MakerOptions();

        ReportProgress(new MakerProgress
        {
            Phase = MakerPhase.Decomposing,
            TaskId = taskId,
            Message = "Decomposing into subtasks with worker agents",
            Depth = depth
        });

        var decompPrompt = _decomposer.BuildDecompositionPrompt(description, context);
        var (decompResult, bestDecomp, decompSession) = await RunVotingWithWorkersAsync(
            taskId, decompPrompt, isSolution: false, options, depth, ct);

        if (decompSession != null)
        {
            sessions.Add(decompSession);
        }

        // MAKER Paper: If decomposition voting fails, try best candidate as fallback
        var decompositionToUse = decompResult ?? bestDecomp;
        
        var steps = string.IsNullOrWhiteSpace(decompositionToUse)
            ? new List<(string, string)>()
            : _decomposer.ParseDecomposition(decompositionToUse).ToList();

        if (steps.Count == 0)
        {
            AddRedFlag(taskId, "Decomposition produced no valid steps, falling back to direct solution");
            return await SolveAtomicTaskAsync(taskId, description, context, depth, ct);
        }
        
        if (decompResult == null && bestDecomp != null)
        {
            AddRedFlag(taskId, $"Decomposition consensus failed, using best candidate with {decompSession?.Candidates.FirstOrDefault()?.Votes ?? 0} votes");
        }

        ReportProgress(new MakerProgress
        {
            Phase = MakerPhase.Executing,
            TaskId = taskId,
            Message = $"Executing {steps.Count} subtasks",
            Depth = depth
        });

        var subtaskResults = new Dictionary<string, string>();
        var childContext = new Dictionary<string, string>(context);

        foreach (var (stepId, stepDescription) in steps)
        {
            var childTaskId = $"{taskId}:{stepId}";
            var (childResult, childNode) = await ExecuteTaskRecursiveAsync(
                childTaskId, stepDescription, depth + 1, childContext, ct);

            children.Add(childNode);

            if (childResult != null)
            {
                subtaskResults[stepId] = childResult;
                childContext[$"result_{stepId}"] = childResult;
            }
        }

        ReportProgress(new MakerProgress
        {
            Phase = MakerPhase.Composing,
            TaskId = taskId,
            Message = "Composing subtask results",
            Depth = depth
        });

        var composed = _composer.Compose(description, subtaskResults, context);

        if (composed == null)
        {
            var synthPrompt = _composer.BuildSynthesisPrompt(description, subtaskResults, context);
            var response = await GenerateResponseAsync(synthPrompt, ct);
            composed = response.Content?.Trim() ?? string.Join("\n\n", subtaskResults.Values);
            CustomState.TotalLlmCalls++;
        }

        return (composed, new TaskNode
        {
            TaskId = taskId,
            Description = description,
            Depth = depth,
            IsAtomic = false,
            Result = composed,
            VotingSessions = sessions,
            Children = children
        });
    }

    #endregion

    #region Voting with Workers (Event-Driven)

    /// <summary>
    /// Run single-round voting with worker agents.
    /// 
    /// MAKER Paper Design:
    /// - Single round of N parallel LLM calls
    /// - If K+ proposals reach consensus → Accept result
    /// - If no consensus → Task is too complex, needs decomposition (NOT retry!)
    /// 
    /// Returns: (Result, BestCandidate, Session)
    /// - Result: The winning content if consensus reached, null otherwise
    /// - BestCandidate: The best candidate even if no consensus (for fallback)
    /// - Session: Voting session details
    /// </summary>
    private async Task<(string? Result, string? BestCandidate, VotingSession? Session)> RunVotingWithWorkersAsync(
        string taskId,
        string prompt,
        bool isSolution,
        MakerOptions options,
        int depth,
        CancellationToken ct)
    {
        // Get embedding generator from AIGAgentBase (configured during InitializeAsync)
        TryGetEmbeddingGenerator(out var embeddingGenerator);
        
        // VoteEngine with maxRounds=1 (single round per MAKER paper)
        var engine = new VoteEngine(
            options.ConsensusK,
            embeddingGenerator,
            maxRounds: 1,  // MAKER paper: single round voting
            options.SemanticSimilarityThreshold);

        var systemPrompt = isSolution
            ? "You are a precise problem solver. Provide clear, direct answers."
            : "You are a precise task decomposition agent. Output ONLY valid JSON.";

        var maxTokens = isSolution ? 2048 : 1024;
        var baseTemperature = isSolution ? 0.2f : 0.3f;

        // Report voting start
        ReportProgress(new MakerProgress
        {
            Phase = MakerPhase.Voting,
            TaskId = taskId,
            Message = $"Dispatching {_expectedWorkers} parallel LLM requests for {(isSolution ? "solution" : "decomposition")}",
            Depth = depth,
            Voting = new VotingProgress
            {
                Type = isSolution ? VotingType.Solution : VotingType.Decomposition,
                Round = 1,
                TotalVotes = 0,
                VotesNeeded = options.ConsensusK,
                LeaderVotes = 0,
                RunnerUpVotes = 0,
                ClusterCount = 0,
                UsedSemanticClustering = embeddingGenerator != null
            }
        });
        
        // Generate unique request prefix
        var requestPrefix = $"{taskId}:";
        _currentVotingRequestPrefix = requestPrefix;
        _collectedProposals.Clear();
        CustomState.PendingProposals = 0;

        // Setup completion source
        _votingCompletionSource = new TaskCompletionSource<List<ProposalResult>>();

        // Dispatch requests to all workers via events (DOWN to children)
        for (var i = 0; i < _expectedWorkers; i++)
        {
            var requestId = $"{requestPrefix}W{i}";
            var workerTemp = DecorrelateTemperature(baseTemperature, i, options.TemperatureVariance);

            await PublishAsync(new GenerateProposalRequest
            {
                RequestId = requestId,
                TaskId = taskId,
                SystemPrompt = systemPrompt,
                UserPrompt = prompt,
                Temperature = workerTemp,
                MaxTokens = maxTokens,
                IsDecomposition = !isSolution
            }, EventDirection.Down);
        }

        // Wait for proposals (with timeout)
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(60));

        List<ProposalResult> proposals;
        try
        {
            proposals = await _votingCompletionSource.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Logger.LogWarning("Voting timed out with {Count}/{Expected} proposals",
                _collectedProposals.Count, _expectedWorkers);
            proposals = _collectedProposals.ToList();
        }

        // Process collected proposals
        VoteResult? voteResult = null;
        var proposalCount = 0;
        
        foreach (var result in proposals)
        {
            proposalCount++;
            CustomState.TotalLlmCalls++;

            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Voting,
                TaskId = taskId,
                Message = $"Received proposal #{proposalCount}/{_expectedWorkers} from {result.WorkerId}",
                Depth = depth,
                Proposal = new LLMProposal
                {
                    ProposalId = result.ProposalId,
                    Content = result.Content,
                    Success = result.Success,
                    Error = result.Error
                }
            });

            if (!result.Success || string.IsNullOrWhiteSpace(result.Content))
                continue;

            var cleanedContent = isSolution
                ? _solver.ExtractSolution(result.Content)
                : result.Content;

            voteResult = await engine.SubmitVoteAsync(cleanedContent, ct);

            var progress = engine.GetProgress(isSolution ? VotingType.Solution : VotingType.Decomposition);

            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Voting,
                TaskId = taskId,
                Message = voteResult != null
                    ? $"✓ Consensus reached! K={options.ConsensusK} votes achieved"
                    : $"Clustering proposals (clusters: {progress.ClusterCount}, leader: {progress.LeaderVotes})",
                Depth = depth,
                Voting = progress
            });

            if (voteResult != null)
                break;
        }

        _currentVotingRequestPrefix = null;

        // Check final consensus state
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

        if (voteResult?.Success != true)
        {
            // Per MAKER paper: No consensus means task is too complex
            var reason = $"No consensus (K={options.ConsensusK} needed, best={bestCandidate?.Votes ?? 0})";
            Logger.LogInformation("Task {TaskId}: {Reason} - task may need decomposition", taskId, reason);
            // Don't add RedFlag here - let the caller decide whether to decompose or use fallback
        }
        else
        {
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

    #endregion

    #region Helpers

    private static float DecorrelateTemperature(float baseTemp, int index, float variance)
    {
        var offset = (variance * index) - (variance * 0.5f);
        return Math.Clamp(baseTemp + offset, 0.0f, 2.0f);
    }

    private void ReportProgress(MakerProgress progress)
    {
        _progressCallback?.Invoke(progress);

        _ = PublishAsync(new MakerProgressEvent
        {
            ExecutionId = CustomState.ExecutionId,
            TaskId = progress.TaskId,
            Phase = progress.Phase.ToString().ToLowerInvariant(),
            Message = progress.Message,
            Depth = progress.Depth,
            Timestamp = Timestamp.FromDateTimeOffset(progress.Timestamp),
            VotingRound = progress.Voting?.Round ?? 0,
            VotingTotal = progress.Voting?.TotalVotes ?? 0,
            VotingLeader = progress.Voting?.LeaderVotes ?? 0,
            VotingRunnerUp = progress.Voting?.RunnerUpVotes ?? 0,
            VotingNeeded = progress.Voting?.VotesNeeded ?? 0,
            ProposalId = progress.Proposal?.ProposalId ?? string.Empty,
            ProposalContent = progress.Proposal?.Content ?? string.Empty,
            ProposalSuccess = progress.Proposal?.Success ?? false
        });
    }

    private void AddRedFlag(string taskId, string reason)
    {
        _redFlags.Add(new RedFlagEvent
        {
            TaskId = taskId,
            Reason = reason,
            Recovered = false
        });
        CustomState.RedFlagReasons.Add($"{taskId}: {reason}");
    }

    #endregion
}
