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
    private IRedFlagStrategy _redFlagStrategy = new DefaultEnglishRedFlagStrategy();
    
    // Runtime state (not persisted)
    private readonly Stopwatch _stopwatch = new();
    private readonly List<RedFlagEvent> _redFlags = [];
    private Action<MakerProgress>? _progressCallback;
    
    // Async coordination
    private TaskCompletionSource<bool>? _initCompletionSource;
    private TaskCompletionSource<VoteResult>? _consensusCompletionSource;
    private CancellationTokenSource? _votingCts;
    private VoteEngine? _currentVoteEngine;
    private readonly ConcurrentBag<ProposalResult> _collectedProposals = [];
    private int _workersInitialized;
    private int _expectedWorkers;
    private int _activeWorkerRequests;
    private string? _currentVotingRequestPrefix;
    private bool _isSolutionVoting;
    private int _currentDepth;
    
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
        IRedFlagStrategy? redFlagStrategy = null,
        RedFlagOptions? redFlagOptions = null,
        Action<MakerProgress>? progressCallback = null)
    {
        _decomposer = decomposer ?? new DefaultDecomposer();
        _solver = solver ?? new DefaultSolver();
        _composer = composer ?? new DefaultComposer();
        _redFlagStrategy = redFlagStrategy ?? new DefaultEnglishRedFlagStrategy(redFlagOptions ?? new RedFlagOptions());
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
                TotalTokens = CustomState.TotalTokensUsed,
                PromptTokens = CustomState.TotalPromptTokens,
                CompletionTokens = CustomState.TotalCompletionTokens,
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
    
    /// <summary>
    /// Get total tokens consumed.
    /// </summary>
    public long GetTotalTokens() => CustomState.TotalTokensUsed;
    
    /// <summary>
    /// Get prompt tokens consumed.
    /// </summary>
    public long GetPromptTokens() => CustomState.TotalPromptTokens;
    
    /// <summary>
    /// Get completion tokens consumed.
    /// </summary>
    public long GetCompletionTokens() => CustomState.TotalCompletionTokens;

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
            CustomState.ConsensusK = request.ConsensusK;
            CustomState.SamplesPerRound = request.SamplesPerRound;
            CustomState.Status = 1; // Starting
            CustomState.StartedAt = Timestamp.FromDateTime(DateTime.UtcNow);
            CustomState.TotalLlmCalls = 0;
            CustomState.RedFlagReasons.Clear();
            _redFlags.Clear();
            
            // Budget limits
            CustomState.MaxTotalLlmCalls = request.MaxTotalLlmCalls > 0 ? request.MaxTotalLlmCalls : 100;
            CustomState.MaxTotalTokens = request.MaxTotalTokens > 0 ? request.MaxTotalTokens : 500_000;
            CustomState.MaxDurationMs = request.MaxDurationMs > 0 ? request.MaxDurationMs : 600_000; // 10 min

            // Setup config
            CustomConfig.DefaultConsensusK = request.ConsensusK;
            CustomConfig.BaseTemperature = request.BaseTemperature;
            CustomConfig.TemperatureVariance = request.TemperatureVariance;
            CustomConfig.SemanticSimilarityThreshold = request.SemanticSimilarityThreshold;
            CustomConfig.ClusteringMethod = request.ClusteringMethod;
            CustomConfig.LlmProviderName = request.ProviderName;
            CustomConfig.WorkerPoolSize = request.SamplesPerRound;
            CustomConfig.MaxTotalLlmCalls = CustomState.MaxTotalLlmCalls;
            CustomConfig.MaxTotalTokens = CustomState.MaxTotalTokens;
            CustomConfig.MaxDurationMs = CustomState.MaxDurationMs;
            CustomConfig.DepthWarningThreshold = request.DepthWarningThreshold > 0 ? request.DepthWarningThreshold : 10;

            // Store options for later use
            _currentOptions = new MakerOptions
            {
                CustomK = request.ConsensusK,
                MaxTotalLlmCalls = CustomState.MaxTotalLlmCalls,
                MaxTotalTokens = CustomState.MaxTotalTokens,
                MaxDuration = TimeSpan.FromMilliseconds(CustomState.MaxDurationMs),
                DepthWarningThreshold = CustomConfig.DepthWarningThreshold,
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
                Message = $"MAKER System Online - K={request.ConsensusK}, N={request.SamplesPerRound}, Budget: {CustomState.MaxTotalLlmCalls} calls / {CustomState.MaxTotalTokens:N0} tokens",
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
                    RedFlags = _redFlags.ToList(),
                    TotalTokens = CustomState.TotalTokensUsed,
                    PromptTokens = CustomState.TotalPromptTokens,
                    CompletionTokens = CustomState.TotalCompletionTokens
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
                    RedFlags = _redFlags.ToList(),
                    TotalTokens = CustomState.TotalTokensUsed,
                    PromptTokens = CustomState.TotalPromptTokens,
                    CompletionTokens = CustomState.TotalCompletionTokens
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
    /// Handle proposal results from workers - STREAMING RACE PATTERN.
    /// Each proposal is immediately voted, consensus triggers early termination.
    /// </summary>
    [EventHandler]
    public async Task HandleProposalResult(ProposalResult result)
    {
        Logger.LogDebug("Received proposal {ProposalId} from worker {WorkerId} for task {TaskId}",
            result.ProposalId, result.WorkerId, result.TaskId);

        // Check if this proposal belongs to current voting session
        if (_currentVotingRequestPrefix == null || !result.RequestId.StartsWith(_currentVotingRequestPrefix))
        {
            return;
        }
        
        // Check if voting already completed (early termination)
        if (_consensusCompletionSource?.Task.IsCompleted == true)
        {
            Logger.LogDebug("Ignoring late proposal {ProposalId} - consensus already reached", result.ProposalId);
            return;
        }

        _collectedProposals.Add(result);
        CustomState.PendingProposals = _collectedProposals.Count;
        CustomState.TotalLlmCalls++;
        
        // Track token usage if available
        if (result.PromptTokens > 0 || result.CompletionTokens > 0)
        {
            CustomState.TotalPromptTokens += result.PromptTokens;
            CustomState.TotalCompletionTokens += result.CompletionTokens;
            CustomState.TotalTokensUsed = CustomState.TotalPromptTokens + CustomState.TotalCompletionTokens;
        }

        // Report progress
        ReportProgress(new MakerProgress
        {
            Phase = MakerPhase.Voting,
            TaskId = result.TaskId,
            Message = $"Received proposal #{_collectedProposals.Count} from {result.WorkerId}",
            Depth = _currentDepth,
            Proposal = new LLMProposal
            {
                ProposalId = result.ProposalId,
                Content = result.Content,
                Success = result.Success,
                Error = result.Error,
                PromptTokens = result.PromptTokens,
                CompletionTokens = result.CompletionTokens
            }
        });

        // Skip failed proposals
        if (!result.Success || string.IsNullOrWhiteSpace(result.Content))
        {
            Interlocked.Decrement(ref _activeWorkerRequests);
            return;
        }
        
        // RED-FLAGGING PARSER: Validate content before entering vote pool
        var isValid = _redFlagStrategy.Validate(result.Content, result.ProposalId, out var redFlagReason);
        if (!isValid)
        {
            AddRedFlag(result.TaskId, redFlagReason!);
            
            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.RedFlag,
                TaskId = result.TaskId,
                Message = $"🚩 Proposal {result.ProposalId} rejected: {redFlagReason}",
                Depth = _currentDepth
            });
            
            Interlocked.Decrement(ref _activeWorkerRequests);
            return;
        }

        // STREAMING RACE: Immediately submit vote
        if (_currentVoteEngine != null)
        {
            var cleanedContent = _isSolutionVoting
                ? _solver.ExtractSolution(result.Content)
                : result.Content;

            var voteResult = await _currentVoteEngine.SubmitVoteAsync(cleanedContent, default);
            
            var progress = _currentVoteEngine.GetProgress(
                _isSolutionVoting ? VotingType.Solution : VotingType.Decomposition);

            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Voting,
                TaskId = result.TaskId,
                Message = voteResult != null
                    ? $"✓ CONSENSUS REACHED after {_collectedProposals.Count} proposals! Early termination."
                    : $"Voting (clusters: {progress.ClusterCount}, leader: {progress.LeaderVotes}/{progress.VotesNeeded})",
                Depth = _currentDepth,
                Voting = progress
            });

            // EARLY TERMINATION: Consensus reached!
            if (voteResult != null)
            {
                Logger.LogInformation(
                    "Early termination: Consensus reached after {Count} proposals (saved waiting for {Remaining} more)",
                    _collectedProposals.Count,
                    _activeWorkerRequests - 1);
                    
                _consensusCompletionSource?.TrySetResult(voteResult);
                
                // Cancel remaining workers
                _votingCts?.Cancel();
                return;
            }
        }

        Interlocked.Decrement(ref _activeWorkerRequests);
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

        var options = _currentOptions ?? new MakerOptions();
        
        // SAFETY NET: Hard depth cap to prevent StackOverflow
        // This is NOT a business limit - it's the "architecture's underwear"
        if (depth >= options.HardDepthCap)
        {
            AddRedFlag(taskId, $"🛑 HARD DEPTH CAP reached ({depth} >= {options.HardDepthCap}). Force-solving as atomic.");
            
            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.RedFlag,
                TaskId = taskId,
                Message = $"🛑 HARD DEPTH CAP ({options.HardDepthCap}) reached. Force-solving to prevent StackOverflow.",
                Depth = depth
            });
            
            // Force solve as atomic - no more decomposition allowed
            var (forceResult, forceNode) = await SolveAtomicTaskAsync(taskId, description, context, depth, ct);
            return (forceResult, forceNode);
        }
        
        // Check budget before proceeding
        var budgetStatus = CheckBudget(options);
        if (!budgetStatus.WithinBudget)
        {
            AddRedFlag(taskId, $"Budget exhausted: {budgetStatus.Reason}");
            
            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.RedFlag,
                TaskId = taskId,
                Message = $"⚠️ Budget exhausted: {budgetStatus.Reason}. Using fallback.",
                Depth = depth
            });
            
            // Return null result - caller will handle fallback
            return (null, new TaskNode
            {
                TaskId = taskId,
                Description = description,
                Depth = depth,
                IsAtomic = true,
                Result = null
            });
        }
        
        // Depth warning (not a hard limit, just informational)
        if (depth >= options.DepthWarningThreshold)
        {
            AddRedFlag(taskId, $"Depth warning: reached depth {depth} (threshold: {options.DepthWarningThreshold})");
            
            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Assessing,
                TaskId = taskId,
                Message = $"⚠️ Deep recursion warning: depth {depth}. Task may be too complex or poorly structured.",
                Depth = depth
            });
        }

        ReportProgress(new MakerProgress
        {
            Phase = MakerPhase.Assessing,
            TaskId = taskId,
            Message = $"[{options.Mode}] Assessing task at depth {depth} (Budget: {CustomState.TotalLlmCalls}/{options.MaxTotalLlmCalls} calls)",
            Depth = depth
        });

        // =================================================================
        //  EXECUTION MODE BRANCHING
        //  - Production: Assess atomicity first, decompose only if needed
        //  - Academic: Force decomposition (paper's "Maximal Decomposition")
        // =================================================================
        
        if (options.Mode == ExecutionMode.Academic)
        {
            // ACADEMIC MODE: Default to decompose, only solve if decomposition fails
            // This follows the paper's Algorithm 4: "Solve(x) -> try Decompose(x) -> fallback to Atomic(x)"
            
            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Decomposing,
                TaskId = taskId,
                Message = $"[Academic] Attempting decomposition first (paper's Maximal Decomposition)",
                Depth = depth
            });
            
            // Try to decompose
            var (decomposeResult, decomposeNode) = await TryDecomposeAsync(taskId, description, context, depth, ct);
            
            if (decomposeResult != null)
            {
                // Decomposition succeeded
                return (decomposeResult, decomposeNode);
            }
            
            // Decomposition failed (no valid subtasks) - treat as atomic
            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Solving,
                TaskId = taskId,
                Message = $"[Academic] Decomposition returned empty/invalid, treating as atomic",
                Depth = depth
            });
            
            return await SolveAtomicTaskAsync(taskId, description, context, depth, ct);
        }
        
        // PRODUCTION MODE: Assess atomicity first (cost-efficient)
        var isAtomic = await AssessAtomicityAsync(description, depth, ct);

        if (isAtomic)
        {
            var (result, node) = await SolveAtomicTaskAsync(taskId, description, context, depth, ct);
            
            if (result != null)
            {
                return (result, node);
            }
            
            // Solution failed - per MAKER paper, this means task is too complex
            // Check if we still have budget to decompose
            var budgetAfterSolve = CheckBudget(options);
            if (budgetAfterSolve.WithinBudget)
            {
                Logger.LogInformation(
                    "Task {TaskId}: Solution voting failed at depth {Depth}, attempting decomposition (MAKER paper: no consensus = needs breakdown)",
                    taskId, depth);
                    
                AddRedFlag(taskId, $"Solution consensus failed at depth {depth}, decomposing further");
                
                return await DecomposeAndExecuteAsync(taskId, description, context, depth, ct);
            }
            
            // Budget exhausted with no consensus - use best candidate as fallback
            if (node.VotingSessions.Count > 0 && node.VotingSessions[0].Candidates.Count > 0)
            {
                var bestCandidate = node.VotingSessions[0].Candidates
                    .OrderByDescending(c => c.Votes)
                    .First();
                    
                AddRedFlag(taskId, $"Budget exhausted without consensus, using best candidate (votes: {bestCandidate.Votes})");
                node.Result = bestCandidate.Content;
                return (bestCandidate.Content, node);
            }
            
            return (null, node);
        }

        return await DecomposeAndExecuteAsync(taskId, description, context, depth, ct);
    }
    
    /// <summary>
    /// Try to decompose a task. Returns null if decomposition fails or produces no valid subtasks.
    /// Used by Academic mode for "default decompose" behavior.
    /// </summary>
    private async Task<(string? Result, TaskNode Node)> TryDecomposeAsync(
        string taskId,
        string description,
        Dictionary<string, string> context,
        int depth,
        CancellationToken ct)
    {
        try
        {
            return await DecomposeAndExecuteAsync(taskId, description, context, depth, ct);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Decomposition failed for task {TaskId}, will treat as atomic", taskId);
            return (null, new TaskNode
            {
                TaskId = taskId,
                Description = description,
                Depth = depth,
                IsAtomic = true,
                Result = null
            });
        }
    }
    
    /// <summary>
    /// Check if we're still within budget.
    /// </summary>
    private (bool WithinBudget, string? Reason) CheckBudget(MakerOptions options)
    {
        // Check LLM calls
        if (options.MaxTotalLlmCalls > 0 && CustomState.TotalLlmCalls >= options.MaxTotalLlmCalls)
        {
            return (false, $"LLM calls exhausted ({CustomState.TotalLlmCalls}/{options.MaxTotalLlmCalls})");
        }
        
        // Check tokens
        if (options.MaxTotalTokens > 0 && CustomState.TotalTokensUsed >= options.MaxTotalTokens)
        {
            return (false, $"Token budget exhausted ({CustomState.TotalTokensUsed:N0}/{options.MaxTotalTokens:N0})");
        }
        
        // Check duration
        if (options.MaxDuration > TimeSpan.Zero && _stopwatch.Elapsed >= options.MaxDuration)
        {
            return (false, $"Time limit reached ({_stopwatch.Elapsed.TotalMinutes:F1} min)");
        }
        
        return (true, null);
    }

    /// <summary>
    /// LLM-based atomicity assessment with strategy override.
    /// Priority: 1. Strategy says atomic → atomic (no LLM needed)
    ///           2. LLM assessment
    ///           3. Fallback heuristics
    /// </summary>
    private async Task<bool> AssessAtomicityAsync(
        string taskDescription,
        int currentDepth,
        CancellationToken ct)
    {
        // PRIORITY 1: Strategy-defined atomicity
        // If the decomposition strategy explicitly says this is atomic, trust it!
        // This prevents infinite decomposition loops where LLM keeps breaking down subtasks.
        if (_decomposer.IsAtomic(taskDescription, currentDepth))
        {
            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Assessing,
                TaskId = CustomState.CurrentTaskId ?? "unknown",
                Message = $"Strategy says ATOMIC at depth {currentDepth} - skipping LLM assessment",
                Depth = currentDepth
            });
            return true;
        }

        // PRIORITY 2: LLM-based assessment with depth context
        var prompt = $$"""
            You are a task complexity analyzer. Determine if the following task can be reliably solved in a SINGLE LLM inference step, or if it needs to be broken down into subtasks.

            IMPORTANT CONTEXT:
            - Current recursion depth: {{currentDepth}}
            - This is a SUB-TASK of a larger problem
            - If this looks like a leaf-level task (e.g., "Summarize Section X", "Calculate Y", "Write paragraph about Z"), it is probably ATOMIC
            - DO NOT decompose tasks that are already specific enough

            Criteria for ATOMIC (return true):
            - Task focuses on ONE specific section/topic/aspect
            - Task can be answered with a single coherent response
            - Task description already includes "Section", "Step", "Part", or similar specificity
            - Task is a direct question or simple generation request

            Criteria for DECOMPOSE (return false):
            - Task requires covering MULTIPLE distinct topics
            - Task explicitly mentions "comprehensive", "complete", "all aspects"
            - Task would benefit from parallel independent subtasks

            Task: {{taskDescription}}

            Respond with ONLY valid JSON (no markdown):
            {"atomic": true, "reason": "..."}
            or
            {"atomic": false, "reason": "..."}
            """;

        try
        {
            var response = await GenerateResponseAsync(prompt, ct);
            CustomState.TotalLlmCalls++;
            
            var content = response.Content?.Trim() ?? "";
            
            // Parse JSON response
            var (isAtomic, reason) = ParseAtomicityResponse(content);
            
            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Assessing,
                TaskId = CustomState.CurrentTaskId ?? "unknown",
                Message = $"LLM atomicity assessment: {(isAtomic ? "ATOMIC" : "DECOMPOSE")} - {reason}",
                Depth = currentDepth
            });
            
            return isAtomic;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Atomicity assessment failed, falling back to heuristic");
            AddRedFlag(CustomState.CurrentTaskId ?? "unknown", $"Atomicity LLM failed: {ex.Message}");
            
            // Fallback to strategy-based assessment (no depth limit)
            return _decomposer.IsAtomic(taskDescription, currentDepth);
        }
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
            Message = $"Decomposing into subtasks (granularity: {options.Granularity})",
            Depth = depth
        });

        // Use granularity-aware decomposition prompt
        var decompPrompt = _decomposer.BuildDecompositionPrompt(description, context, options.Granularity);
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
            Message = $"Executing {steps.Count} subtasks (context isolation: {options.ContextIsolation})",
            Depth = depth
        });

        var subtaskResults = new Dictionary<string, string>();

        foreach (var (stepId, stepDescription) in steps)
        {
            // Apply context isolation mode
            var childContext = BuildChildContext(context, subtaskResults, stepId, options.ContextIsolation);
            
            var childTaskId = $"{taskId}:{stepId}";
            var (childResult, childNode) = await ExecuteTaskRecursiveAsync(
                childTaskId, stepDescription, depth + 1, childContext, ct);

            children.Add(childNode);

            if (childResult != null)
            {
                subtaskResults[stepId] = childResult;
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
    
    /// <summary>
    /// Build child context based on isolation mode.
    /// </summary>
    private static Dictionary<string, string> BuildChildContext(
        Dictionary<string, string> parentContext,
        Dictionary<string, string> siblingResults,
        string currentStepId,
        ContextIsolationMode isolationMode)
    {
        return isolationMode switch
        {
            // Full: Inherit everything from parent + all sibling results
            ContextIsolationMode.Full => new Dictionary<string, string>(parentContext)
                .Concat(siblingResults.Select(kv => new KeyValuePair<string, string>($"result_{kv.Key}", kv.Value)))
                .GroupBy(kv => kv.Key)
                .ToDictionary(g => g.Key, g => g.Last().Value),
            
            // Minimal: Only inherit explicit domain context (keys without "result_" prefix)
            // + only the immediately previous sibling's result
            ContextIsolationMode.Minimal => BuildMinimalContext(parentContext, siblingResults, currentStepId),
            
            // None: Start completely fresh - no inheritance
            ContextIsolationMode.None => new Dictionary<string, string>(),
            
            _ => new Dictionary<string, string>(parentContext)
        };
    }
    
    /// <summary>
    /// Build minimal context - only essential domain info + previous step's result.
    /// </summary>
    private static Dictionary<string, string> BuildMinimalContext(
        Dictionary<string, string> parentContext,
        Dictionary<string, string> siblingResults,
        string currentStepId)
    {
        var result = new Dictionary<string, string>();
        
        // Only inherit non-result keys from parent (domain context like "language", "style")
        foreach (var kv in parentContext)
        {
            if (!kv.Key.StartsWith("result_", StringComparison.OrdinalIgnoreCase))
            {
                result[kv.Key] = kv.Value;
            }
        }
        
        // Only include the immediately previous sibling's result (if any)
        // This maintains minimal sequential dependency
        if (siblingResults.Count > 0)
        {
            var lastResult = siblingResults.Last();
            result[$"previous_result"] = lastResult.Value;
        }
        
        return result;
    }

    #endregion

    #region Voting with Workers (Event-Driven, Streaming Race)

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
            maxRounds: 10,  // Allow continuous sampling up to 10 batches
            options.SemanticSimilarityThreshold);

        // Store for streaming race
        _currentVoteEngine = engine;
        _isSolutionVoting = isSolution;
        _currentDepth = depth;

        var systemPrompt = isSolution
            ? "You are a precise problem solver. Provide clear, direct answers."
            : "You are a precise task decomposition agent. Output ONLY valid JSON.";

        var maxTokens = isSolution ? 2048 : 1024;
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
                }, EventDirection.Down);
            }
            
            totalSamplesSent += batchSize;

            // Wait for either:
            // 1. Consensus reached (early termination) - FAST PATH
            // 2. Timeout (per batch)
            // 3. Cancellation
            try
            {
                using var batchCts = CancellationTokenSource.CreateLinkedTokenSource(_votingCts.Token);
                batchCts.CancelAfter(TimeSpan.FromSeconds(30)); // 30s per batch
                
                voteResult = await _consensusCompletionSource.Task.WaitAsync(batchCts.Token);
                
                Logger.LogInformation(
                    "EARLY TERMINATION: Consensus reached in {Ms}ms after {Samples} samples",
                    _stopwatch.ElapsedMilliseconds,
                    _collectedProposals.Count);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Batch timeout - check if we should continue sampling
                voteResult = engine.CheckConsensus();
                
                if (voteResult == null && totalSamplesSent < maxTotalSamples)
                {
                    // No consensus yet, prepare next batch
                    round++;
                    requestPrefix = $"{taskId}:R{round}:";
                    _currentVotingRequestPrefix = requestPrefix;
                    _consensusCompletionSource = new TaskCompletionSource<VoteResult>();
                    
                    var progress = engine.GetProgress(isSolution ? VotingType.Solution : VotingType.Decomposition);
                    
                    ReportProgress(new MakerProgress
                    {
                        Phase = MakerPhase.Voting,
                        TaskId = taskId,
                        Message = $"No consensus in batch {round - 1}, dispatching more workers (continuous sampling)",
                        Depth = depth,
                        Voting = progress
                    });
                }
            }
        }

        // Cleanup
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
    
    // ============================================================
    //  RED-FLAGGING is now handled by IRedFlagStrategy (injected)
    //  See: IRedFlagStrategy.cs for available implementations:
    //  - DefaultEnglishRedFlagStrategy (default)
    //  - ChineseRedFlagStrategy
    //  - CodeAwareRedFlagStrategy
    //  - NoOpRedFlagStrategy (disable checking)
    // ============================================================
    
    /// <summary>
    /// Parse atomicity assessment JSON response.
    /// Returns (isAtomic, reason).
    /// </summary>
    private static (bool IsAtomic, string Reason) ParseAtomicityResponse(string content)
    {
        // Try to extract JSON from content (handle markdown wrapping)
        var json = content.Trim();
        if (json.StartsWith("```"))
        {
            var start = json.IndexOf('{');
            var end = json.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                json = json.Substring(start, end - start + 1);
            }
        }
        
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            var isAtomic = root.TryGetProperty("atomic", out var atomicProp) && atomicProp.GetBoolean();
            var reason = root.TryGetProperty("reason", out var reasonProp) 
                ? reasonProp.GetString() ?? "no reason provided"
                : "no reason provided";
            
            return (isAtomic, reason);
        }
        catch (JsonException)
        {
            // Fallback: check for keywords
            var upper = content.ToUpperInvariant();
            if (upper.Contains("\"ATOMIC\"") || upper.Contains(":TRUE") || upper.Contains(": TRUE"))
            {
                return (true, "parsed from keywords");
            }
            return (false, "failed to parse JSON, defaulting to decompose");
        }
    }

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
