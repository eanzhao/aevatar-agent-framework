using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Maker.Messages;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Maker.Agents;

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

    public MakerCoordinatorGAgent()
    {
    }

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

    // ============================================================
    //  P0-2: State Persistence Methods
    // ============================================================

    /// <summary>
    /// Persist current runtime state to protobuf state for recovery after restart.
    /// Call this periodically during long-running executions.
    /// </summary>
    public void PersistRuntimeState()
    {
        // Save execution context
        if (_currentContext != null)
        {
            CustomState.ExecutionContext.Clear();
            foreach (var kvp in _currentContext)
            {
                CustomState.ExecutionContext[kvp.Key] = kvp.Value;
            }
        }

        // Save MakerOptions
        if (_currentOptions != null)
        {
            CustomState.Options = OptionsToProto(_currentOptions);
        }

        // Save result if completed
        if (_cachedResult != null)
        {
            CustomState.ResultContent = _cachedResult.Content;
            CustomState.ResultError = _cachedResult.Error ?? string.Empty;
            CustomState.ResultTraceJson = JsonSerializer.Serialize(_cachedResult.Trace);
        }

        // Save elapsed time
        CustomState.ElapsedMs = (long)_stopwatch.Elapsed.TotalMilliseconds;

        CustomState.UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);

        Logger.LogDebug("Runtime state persisted for execution {ExecutionId}", CustomState.ExecutionId);
    }

    /// <summary>
    /// Restore runtime state from persisted protobuf state.
    /// Call this in OnActivateAsync to resume interrupted executions.
    /// </summary>
    public void RestoreRuntimeState()
    {
        // Restore execution context
        if (CustomState.ExecutionContext.Count > 0)
        {
            _currentContext = new Dictionary<string, string>(CustomState.ExecutionContext);
        }

        // Restore MakerOptions
        if (CustomState.Options != null)
        {
            _currentOptions = ProtoToOptions(CustomState.Options);
        }

        // Restore cached result if completed
        if (!string.IsNullOrEmpty(CustomState.ResultContent) || !string.IsNullOrEmpty(CustomState.ResultError))
        {
            var defaultTrace = new MakerTrace
            {
                ExecutionId = CustomState.ExecutionId,
                RootTask = new TaskNode
                {
                    TaskId = CustomState.ExecutionId,
                    Description = CustomState.TaskDescription
                }
            };

            MakerTrace? deserializedTrace = null;
            if (!string.IsNullOrEmpty(CustomState.ResultTraceJson))
            {
                try
                {
                    deserializedTrace = JsonSerializer.Deserialize<MakerTrace>(CustomState.ResultTraceJson);
                }
                catch
                {
                    // Ignore deserialization errors, use default
                }
            }

            _cachedResult = new MakerResult
            {
                Success = CustomState.Status == 3,
                Content = CustomState.ResultContent,
                Error = string.IsNullOrEmpty(CustomState.ResultError) ? null : CustomState.ResultError,
                Trace = deserializedTrace ?? defaultTrace
            };
        }

        // Restore red flags from state
        foreach (var reason in CustomState.RedFlagReasons)
        {
            var parts = reason.Split(": ", 2);
            _redFlags.Add(new RedFlagEvent
            {
                TaskId = parts.Length > 1 ? parts[0] : "unknown",
                Reason = parts.Length > 1 ? parts[1] : reason,
                Recovered = false
            });
        }

        Logger.LogDebug("Runtime state restored for execution {ExecutionId}, status={Status}",
            CustomState.ExecutionId, CustomState.Status);
    }

    /// <summary>
    /// Check if there's an interrupted execution that can be resumed.
    /// </summary>
    public bool HasInterruptedExecution()
    {
        // Status 1=starting, 2=running means interrupted
        return CustomState.Status is 1 or 2 && !string.IsNullOrEmpty(CustomState.ExecutionId);
    }

    /// <summary>
    /// Convert MakerOptions to proto representation.
    /// </summary>
    private static MakerOptionsProto OptionsToProto(MakerOptions options)
    {
        return new MakerOptionsProto
        {
            ConsensusK = options.ConsensusK,
            SamplesPerRound = options.SamplesPerRound,
            MaxTotalLlmCalls = options.MaxTotalLlmCalls,
            MaxTotalTokens = options.MaxTotalTokens,
            MaxDurationMs = (long)options.MaxDuration.TotalMilliseconds,
            DepthWarningThreshold = options.DepthWarningThreshold,
            HardDepthCap = options.HardDepthCap,
            BaseTemperature = options.BaseTemperature,
            TemperatureVariance = options.TemperatureVariance,
            SemanticSimilarityThreshold = options.SemanticSimilarityThreshold,
            ClusteringMethod = options.ClusteringMethod ?? "semantic",
            ExecutionMode = options.Mode.ToString()
        };
    }

    /// <summary>
    /// Convert proto representation back to MakerOptions.
    /// </summary>
    private static MakerOptions ProtoToOptions(MakerOptionsProto proto)
    {
        var mode = System.Enum.TryParse<ExecutionMode>(proto.ExecutionMode, out var parsedMode)
            ? parsedMode
            : ExecutionMode.Production;

        return new MakerOptions
        {
            CustomK = proto.ConsensusK > 0 ? proto.ConsensusK : 3,
            MaxTotalLlmCalls = proto.MaxTotalLlmCalls > 0 ? proto.MaxTotalLlmCalls : 100,
            MaxTotalTokens = proto.MaxTotalTokens > 0 ? proto.MaxTotalTokens : 500_000,
            MaxDuration = proto.MaxDurationMs > 0
                ? TimeSpan.FromMilliseconds(proto.MaxDurationMs)
                : TimeSpan.FromMinutes(10),
            DepthWarningThreshold = proto.DepthWarningThreshold > 0 ? proto.DepthWarningThreshold : 10,
            HardDepthCap = proto.HardDepthCap > 0 ? proto.HardDepthCap : 50,
            BaseTemperature = proto.BaseTemperature > 0 ? proto.BaseTemperature : 0.7f,
            TemperatureVariance = proto.TemperatureVariance,
            SemanticSimilarityThreshold = proto.SemanticSimilarityThreshold > 0
                ? proto.SemanticSimilarityThreshold
                : 0.85f,
            ClusteringMethod = !string.IsNullOrEmpty(proto.ClusteringMethod)
                ? proto.ClusteringMethod
                : "semantic",
            Mode = mode
        };
    }

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
                Message =
                    $"MAKER System Online - K={request.ConsensusK}, N={request.SamplesPerRound}, Budget: {CustomState.MaxTotalLlmCalls} calls / {CustomState.MaxTotalTokens:N0} tokens",
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

            // Discover and validate LLM providers
            var (validProviders, coordinatorProvider) = await DiscoverAndValidateProvidersAsync(
                request.UseMultipleProviders,
                request.CoordinatorProviderName,
                request.ProviderName,
                request.ExecutionId);

            // Re-initialize Coordinator with the determined provider (if different from default)
            if (coordinatorProvider != request.ProviderName)
            {
                await InitializeAsync(coordinatorProvider, config =>
                {
                    config.Temperature = request.BaseTemperature;
                    config.MaxOutputTokens = 4096;
                });
                Logger.LogInformation("Coordinator re-initialized with provider: {Provider}", coordinatorProvider);
            }

            // Send initialization requests to workers (DOWN to children)
            // Round-robin provider assignment for decorrelation
            for (var i = 0; i < request.SamplesPerRound; i++)
            {
                var workerTemp = DecorrelateTemperature(request.BaseTemperature, i, request.TemperatureVariance);
                var workerProvider = validProviders[i % validProviders.Count];

                await PublishAsync(new InitializeWorkerRequest
                {
                    CoordinatorId = Id.ToString(),
                    ProviderName = workerProvider,
                    WorkerIndex = i,
                    Temperature = workerTemp
                }, EventDirection.Down);

                Logger.LogDebug("Worker {Index} assigned to provider: {Provider}", i, workerProvider);
            }

            // Wait for all workers to initialize (with timeout)
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await _initCompletionSource.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException(
                    $"Workers failed to initialize within timeout. Got {_workersInitialized}/{_expectedWorkers}");
            }

            Logger.LogInformation("All {Count} workers initialized", _expectedWorkers);

            // Start execution
            CustomState.Status = 2; // Running

            var (result, node) = await ExecuteTaskAsync(
                taskId: request.ExecutionId,
                description: request.TaskDescription,
                depth: 0,
                context: _currentContext,
                ct: CancellationToken.None);

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

            // P0-2: Persist final state for recovery/audit
            PersistRuntimeState();

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

        // Report progress with provider info
        var providerLabel = string.IsNullOrEmpty(result.ProviderName) ? "" : $" [{result.ProviderName}]";
        ReportProgress(new MakerProgress
        {
            Phase = MakerPhase.Voting,
            TaskId = result.TaskId,
            Message = $"Received proposal #{_collectedProposals.Count} from {result.WorkerId}{providerLabel}",
            Depth = _currentDepth,
            Proposal = new LLMProposal
            {
                ProposalId = result.ProposalId,
                Content = result.Content,
                Success = result.Success,
                Error = result.Error,
                PromptTokens = result.PromptTokens,
                CompletionTokens = result.CompletionTokens,
                ProviderName = result.ProviderName
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

                // Cancel remaining workers - both local CTS and remote LLM calls
                _votingCts?.Cancel();

                // Notify workers to cancel their in-progress LLM calls (saves tokens!)
                await PublishAsync(new CancelCurrentRequest
                {
                    CoordinatorId = Id.ToString(),
                    Reason = "consensus_reached"
                }, EventDirection.Down);

                return;
            }
        }

        Interlocked.Decrement(ref _activeWorkerRequests);
    }

    #endregion

    #region Task Execution

    // ============================================================
    //  Iterative Task Execution
    //  Uses explicit stack to simulate recursion
    // ============================================================

    /// <summary>
    /// Task execution state for iterative processing.
    /// </summary>
    private enum TaskExecutionPhase
    {
        Pending, // Not yet started
        Assessing, // Assessing atomicity
        Solving, // Solving as atomic
        Decomposing, // Running decomposition voting
        ExecutingChildren, // Executing subtasks
        Composing, // Composing results
        Completed // Done
    }

    /// <summary>
    /// Execution context for a single task in the iterative stack.
    /// </summary>
    private sealed class TaskExecutionContext
    {
        public required string TaskId { get; init; }
        public required string Description { get; init; }
        public required int Depth { get; init; }
        public required Dictionary<string, string> Context { get; init; }

        public TaskExecutionPhase Phase { get; set; } = TaskExecutionPhase.Pending;
        public TaskNode? Node { get; set; }
        public string? Result { get; set; }

        // For decomposition
        public List<(string StepId, string Description)>? Subtasks { get; set; }
        public int CurrentSubtaskIndex { get; set; }
        public Dictionary<string, string> SubtaskResults { get; } = new();
        public List<TaskNode> ChildNodes { get; } = [];
        public List<VotingSession> VotingSessions { get; } = [];

        // For atomic solve fallback
        public bool SolveFailed { get; set; }
        public string? BestCandidate { get; set; }
    }

    /// <summary>
    /// Execute task using iterative approach (stack-based, no recursion).
    /// This prevents StackOverflowException for deeply nested task trees.
    /// 
    /// Uses an explicit stack to manage task execution states.
    /// </summary>
    private async Task<(string? Result, TaskNode Node)> ExecuteTaskAsync(
        string taskId,
        string description,
        int depth,
        Dictionary<string, string> context,
        CancellationToken ct)
    {
        var options = _currentOptions ?? new MakerOptions();

        // Task execution stack - replaces call stack
        var taskStack = new Stack<TaskExecutionContext>();

        // Results storage - maps taskId to (Result, Node)
        var completedTasks = new Dictionary<string, (string? Result, TaskNode Node)>();

        // Push initial task
        taskStack.Push(new TaskExecutionContext
        {
            TaskId = taskId,
            Description = description,
            Depth = depth,
            Context = context,
            Phase = TaskExecutionPhase.Pending
        });

        // Main execution loop - process tasks until stack is empty
        while (taskStack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var current = taskStack.Peek();
            CustomState.CurrentTaskId = current.TaskId;
            CustomState.CurrentDepth = current.Depth;

            switch (current.Phase)
            {
                case TaskExecutionPhase.Pending:
                    // Initialize task node
                    current.Node = new TaskNode
                    {
                        TaskId = current.TaskId,
                        Description = current.Description,
                        Depth = current.Depth,
                        IsAtomic = false
                    };

                    // Hard depth cap check (safety net)
                    if (current.Depth >= options.HardDepthCap)
                    {
                        AddRedFlag(current.TaskId, $"🛑 HARD DEPTH CAP reached ({current.Depth})");
                        ReportProgress(new MakerProgress
                        {
                            Phase = MakerPhase.RedFlag,
                            TaskId = current.TaskId,
                            Message = $"🛑 HARD DEPTH CAP ({options.HardDepthCap}) reached.",
                            Depth = current.Depth
                        });

                        // Force solve as atomic
                        var (forceResult, forceNode) = await SolveAtomicTaskAsync(
                            current.TaskId, current.Description, current.Context, current.Depth, ct);
                        completedTasks[current.TaskId] = (forceResult, forceNode);
                        taskStack.Pop();
                        continue;
                    }

                    // Budget check
                    var budgetStatus = CheckBudget(options);
                    if (!budgetStatus.WithinBudget)
                    {
                        AddRedFlag(current.TaskId, $"Budget exhausted: {budgetStatus.Reason}");
                        var budgetNode = new TaskNode
                        {
                            TaskId = current.TaskId,
                            Description = current.Description,
                            Depth = current.Depth,
                            IsAtomic = true,
                            Result = null
                        };
                        completedTasks[current.TaskId] = (null, budgetNode);
                        taskStack.Pop();
                        continue;
                    }

                    // Depth warning
                    if (current.Depth >= options.DepthWarningThreshold)
                    {
                        AddRedFlag(current.TaskId, $"Depth warning: {current.Depth}");
                    }

                    current.Phase = TaskExecutionPhase.Assessing;
                    continue;

                case TaskExecutionPhase.Assessing:
                    ReportProgress(new MakerProgress
                    {
                        Phase = MakerPhase.Assessing,
                        TaskId = current.TaskId,
                        Message = $"[{options.Mode}] Assessing at depth {current.Depth}",
                        Depth = current.Depth
                    });

                    if (options.Mode == ExecutionMode.Academic)
                    {
                        // Academic mode: try decomposition first
                        current.Phase = TaskExecutionPhase.Decomposing;
                    }
                    else
                    {
                        // Production mode: assess atomicity
                        var isAtomic = await AssessAtomicityAsync(current.Description, current.Depth, ct);
                        current.Phase = isAtomic ? TaskExecutionPhase.Solving : TaskExecutionPhase.Decomposing;
                    }

                    continue;

                case TaskExecutionPhase.Solving:
                    ReportProgress(new MakerProgress
                    {
                        Phase = MakerPhase.Solving,
                        TaskId = current.TaskId,
                        Message = $"Solving atomic task at depth {current.Depth}",
                        Depth = current.Depth
                    });

                    var (solveResult, solveNode) = await SolveAtomicTaskAsync(
                        current.TaskId, current.Description, current.Context, current.Depth, ct);

                    if (solveResult != null)
                    {
                        completedTasks[current.TaskId] = (solveResult, solveNode);
                        taskStack.Pop();
                        continue;
                    }

                    // Solve failed - try decomposition if budget allows
                    current.BestCandidate = solveNode.VotingSessions.Count > 0
                        ? solveNode.VotingSessions[0].Candidates.OrderByDescending(c => c.Votes).FirstOrDefault()
                            ?.Content
                        : null;
                    current.SolveFailed = true;
                    current.VotingSessions.AddRange(solveNode.VotingSessions);

                    if (CheckBudget(options).WithinBudget)
                    {
                        AddRedFlag(current.TaskId, $"Solution consensus failed, decomposing");
                        current.Phase = TaskExecutionPhase.Decomposing;
                    }
                    else
                    {
                        // Budget exhausted - use best candidate as fallback
                        if (current.BestCandidate != null)
                        {
                            solveNode.Result = current.BestCandidate;
                            completedTasks[current.TaskId] = (current.BestCandidate, solveNode);
                        }
                        else
                        {
                            completedTasks[current.TaskId] = (null, solveNode);
                        }

                        taskStack.Pop();
                    }

                    continue;

                case TaskExecutionPhase.Decomposing:
                    ReportProgress(new MakerProgress
                    {
                        Phase = MakerPhase.Decomposing,
                        TaskId = current.TaskId,
                        Message = $"Decomposing at depth {current.Depth}",
                        Depth = current.Depth
                    });

                    // Get decomposition
                    var decompPrompt = _decomposer.BuildDecompositionPrompt(
                        current.Description, current.Context, options.Granularity);
                    var (decompResult, bestDecomp, decompSession) = await RunVotingWithWorkersAsync(
                        current.TaskId, decompPrompt, isSolution: false, options, current.Depth, ct);

                    if (decompSession != null) current.VotingSessions.Add(decompSession);

                    var decompositionToUse = decompResult ?? bestDecomp;
                    var steps = string.IsNullOrWhiteSpace(decompositionToUse)
                        ? new List<(string, string)>()
                        : _decomposer.ParseDecomposition(decompositionToUse).ToList();

                    if (steps.Count == 0)
                    {
                        // No valid decomposition - solve as atomic
                        if (!current.SolveFailed)
                        {
                            current.Phase = TaskExecutionPhase.Solving;
                            continue;
                        }

                        // Already tried solving, use fallback
                        var fallbackNode = new TaskNode
                        {
                            TaskId = current.TaskId,
                            Description = current.Description,
                            Depth = current.Depth,
                            IsAtomic = true,
                            Result = current.BestCandidate,
                            VotingSessions = current.VotingSessions
                        };
                        completedTasks[current.TaskId] = (current.BestCandidate, fallbackNode);
                        taskStack.Pop();
                        continue;
                    }

                    // Store subtasks and push them to stack (in reverse order)
                    current.Subtasks = steps;
                    current.CurrentSubtaskIndex = 0;
                    current.Phase = TaskExecutionPhase.ExecutingChildren;

                    // Push subtasks in reverse so first subtask is processed first
                    for (var i = steps.Count - 1; i >= 0; i--)
                    {
                        var (stepId, stepDesc) = steps[i];
                        var childContext = BuildChildContext(
                            current.Context, current.SubtaskResults, stepId, options.ContextIsolation);

                        taskStack.Push(new TaskExecutionContext
                        {
                            TaskId = $"{current.TaskId}:{stepId}",
                            Description = stepDesc,
                            Depth = current.Depth + 1,
                            Context = childContext,
                            Phase = TaskExecutionPhase.Pending
                        });
                    }

                    continue;

                case TaskExecutionPhase.ExecutingChildren:
                    // Check if all children are done
                    var allChildrenDone = current.Subtasks!.All(s =>
                        completedTasks.ContainsKey($"{current.TaskId}:{s.StepId}"));

                    if (!allChildrenDone)
                    {
                        // Children still processing - this shouldn't happen with correct stack order
                        // but just in case, skip to let children process
                        continue;
                    }

                    // Collect child results
                    foreach (var (stepId, _) in current.Subtasks!)
                    {
                        var childTaskId = $"{current.TaskId}:{stepId}";
                        if (completedTasks.TryGetValue(childTaskId, out var childResult))
                        {
                            current.ChildNodes.Add(childResult.Node);
                            if (childResult.Result != null)
                            {
                                current.SubtaskResults[stepId] = childResult.Result;
                            }
                        }
                    }

                    current.Phase = TaskExecutionPhase.Composing;
                    continue;

                case TaskExecutionPhase.Composing:
                    ReportProgress(new MakerProgress
                    {
                        Phase = MakerPhase.Composing,
                        TaskId = current.TaskId,
                        Message = "Composing subtask results",
                        Depth = current.Depth
                    });

                    // Compose results
                    var composed = _composer.Compose(current.Description, current.SubtaskResults, current.Context);
                    if (composed == null)
                    {
                        var synthPrompt = _composer.BuildSynthesisPrompt(
                            current.Description, current.SubtaskResults, current.Context);
                        var response = await GenerateResponseAsync(synthPrompt, ct);
                        composed = response.Content?.Trim() ?? string.Join("\n\n", current.SubtaskResults.Values);
                        CustomState.TotalLlmCalls++;
                    }

                    // Create final node with all children and results
                    var composedNode = new TaskNode
                    {
                        TaskId = current.TaskId,
                        Description = current.Description,
                        Depth = current.Depth,
                        IsAtomic = false,
                        Result = composed,
                        Children = current.ChildNodes,
                        VotingSessions = current.VotingSessions
                    };

                    completedTasks[current.TaskId] = (composed, composedNode);
                    taskStack.Pop();
                    continue;

                case TaskExecutionPhase.Completed:
                    taskStack.Pop();
                    continue;
            }
        }

        // Return root task result
        if (completedTasks.TryGetValue(taskId, out var rootResult))
        {
            return rootResult;
        }

        // Fallback - should not reach here
        return (null, new TaskNode
        {
            TaskId = taskId,
            Description = description,
            Depth = depth,
            IsAtomic = true
        });
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
            FallbackCandidate = bestCandidate, // Store for potential fallback use
            VotingSessions = sessions
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
            maxRounds: 10, // Allow continuous sampling up to 10 batches
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
                var workerTemp =
                    DecorrelateTemperature(baseTemperature, totalSamplesSent + i, options.TemperatureVariance);

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
    //  LLM Provider Discovery and Validation
    // ============================================================

    /// <summary>
    /// Discover and validate LLM providers based on configuration.
    /// 
    /// Logic:
    /// 1. If UseMultipleProviders = true, auto-discover all configured providers
    /// 2. Validate each provider with a simple test request
    /// 3. Only use providers that pass validation
    /// 4. Determine Coordinator's provider (dedicated or round-robin)
    /// </summary>
    /// <returns>
    /// Tuple of (validProviders for workers, coordinatorProvider)
    /// </returns>
    private async Task<(List<string> ValidProviders, string CoordinatorProvider)> DiscoverAndValidateProvidersAsync(
        bool useMultipleProviders,
        string? coordinatorProviderName,
        string defaultProviderName,
        string executionId)
    {
        // Step 1: Discover all candidate providers
        List<string> candidateProviders;

        if (useMultipleProviders)
        {
            // Auto-discover all configured providers
            candidateProviders = LLMProviderFactory.GetAvailableProviderNames().ToList();

            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Starting,
                TaskId = executionId,
                Message =
                    $"Multi-provider mode: Discovered {candidateProviders.Count} configured provider(s): {string.Join(", ", candidateProviders)}",
                Depth = 0
            });
        }
        else
        {
            // Single provider mode
            candidateProviders = [defaultProviderName];
        }

        // Add coordinator provider to candidates if specified and not already included
        if (!string.IsNullOrEmpty(coordinatorProviderName) && !candidateProviders.Contains(coordinatorProviderName))
        {
            candidateProviders.Add(coordinatorProviderName);
        }

        // Step 2: Validate all providers
        var validProviders = new List<string>();
        var failedProviders = new List<(string Provider, string Error)>();

        ReportProgress(new MakerProgress
        {
            Phase = MakerPhase.Starting,
            TaskId = executionId,
            Message = $"Validating {candidateProviders.Count} LLM provider(s)...",
            Depth = 0
        });

        foreach (var providerName in candidateProviders)
        {
            var (isValid, error) = await ValidateSingleProviderAsync(providerName, executionId);
            if (isValid)
            {
                validProviders.Add(providerName);
            }
            else
            {
                failedProviders.Add((providerName, error ?? "Unknown error"));
            }
        }

        // Step 3: Check if we have any valid providers
        if (validProviders.Count == 0)
        {
            var errorMessage = $"No valid LLM providers found. Checked: {string.Join(", ", candidateProviders)}";
            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Failed,
                TaskId = executionId,
                Message = errorMessage,
                Depth = 0
            });
            throw new InvalidOperationException(errorMessage);
        }

        // Step 4: Determine Coordinator's provider
        string coordinatorProvider;
        if (!string.IsNullOrEmpty(coordinatorProviderName) && validProviders.Contains(coordinatorProviderName))
        {
            // Use dedicated Coordinator provider
            coordinatorProvider = coordinatorProviderName;
            Logger.LogInformation("Coordinator using dedicated provider: {Provider}", coordinatorProvider);

            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Starting,
                TaskId = executionId,
                Message = $"Coordinator using dedicated provider: {coordinatorProvider}",
                Depth = 0
            });
        }
        else
        {
            // Coordinator participates in round-robin (use first valid provider)
            coordinatorProvider = validProviders[0];
            Logger.LogInformation("Coordinator participating in round-robin, using: {Provider}", coordinatorProvider);
        }

        // Summary with detailed error reasons
        if (failedProviders.Count > 0)
        {
            var failedDetails = string.Join(", ", failedProviders.Select(f => $"{f.Provider}({f.Error})"));
            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Starting,
                TaskId = executionId,
                Message = $"⚠️ {failedProviders.Count} provider(s) failed: {failedDetails}",
                Depth = 0
            });
        }

        ReportProgress(new MakerProgress
        {
            Phase = MakerPhase.Starting,
            TaskId = executionId,
            Message =
                $"✓ {validProviders.Count} valid provider(s) for workers: {string.Join(", ", validProviders)} | Coordinator: {coordinatorProvider}",
            Depth = 0
        });

        return (validProviders, coordinatorProvider);
    }

    /// <summary>
    /// Validate a single LLM provider with a test request.
    /// Returns (IsValid, ErrorReason) tuple.
    /// </summary>
    private async Task<(bool IsValid, string? Error)> ValidateSingleProviderAsync(string providerName,
        string executionId)
    {
        try
        {
            Logger.LogDebug("Validating LLM provider: {Provider}", providerName);

            var provider = await LLMProviderFactory.GetProviderAsync(providerName);

            var testRequest = new Aevatar.Agents.AI.Abstractions.AevatarLLMRequest
            {
                SystemPrompt = "You are a test assistant.",
                Settings = new Aevatar.Agents.AI.Abstractions.AevatarLLMSettings
                {
                    Temperature = 0.1f,
                    MaxTokens = 10
                },
                Messages =
                [
                    new AI.AevatarChatMessage
                    {
                        Role = AI.AevatarChatRole.User,
                        Content = "Reply with 'OK'"
                    }
                ]
            };

            var response = await provider.GenerateAsync(testRequest);

            if (string.IsNullOrWhiteSpace(response.Content))
            {
                // Log detailed response info for debugging
                var debugInfo = $"Content='{response.Content ?? "null"}', " +
                                $"StopReason={response.AevatarStopReason}, " +
                                $"PromptTokens={response.Usage?.PromptTokens}, " +
                                $"CompletionTokens={response.Usage?.CompletionTokens}";
                Logger.LogWarning("Provider {Provider} returned empty response. Details: {Debug}", providerName,
                    debugInfo);

                // Provide actionable error message based on stop reason
                var error = response.AevatarStopReason switch
                {
                    AI.Abstractions.AevatarStopReason.ContentFilter => "Content filtered by safety policy",
                    AI.Abstractions.AevatarStopReason.MaxTokens => "Response truncated (max tokens too low)",
                    AI.Abstractions.AevatarStopReason.Complete => "Empty response (model returned nothing)",
                    AI.Abstractions.AevatarStopReason.Error => "API returned error",
                    AI.Abstractions.AevatarStopReason.Timeout => "Request timeout",
                    AI.Abstractions.AevatarStopReason.RateLimitReached => "Rate limited",
                    _ => $"Empty response (stop_reason: {response.AevatarStopReason})"
                };

                ReportProgress(new MakerProgress
                {
                    Phase = MakerPhase.Starting,
                    TaskId = executionId,
                    Message = $"✗ Provider '{providerName}': {error}",
                    Depth = 0
                });
                return (false, error);
            }

            Logger.LogInformation("Provider {Provider} validated successfully", providerName);
            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Starting,
                TaskId = executionId,
                Message = $"✓ Provider '{providerName}' is available",
                Depth = 0
            });
            return (true, null);
        }
        catch (Exception ex)
        {
            // Extract concise error message
            var error = ExtractConciseError(ex);
            Logger.LogWarning(ex, "Provider {Provider} validation failed: {Message}", providerName, error);
            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Starting,
                TaskId = executionId,
                Message = $"✗ Provider '{providerName}': {error}",
                Depth = 0
            });
            return (false, error);
        }
    }

    /// <summary>
    /// Extract a concise, user-friendly error message from exception.
    /// </summary>
    private static string ExtractConciseError(Exception ex)
    {
        var msg = ex.Message;

        // Common API error patterns
        if (msg.Contains("401") || msg.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase))
            return "Invalid API key";
        if (msg.Contains("403") || msg.Contains("Forbidden", StringComparison.OrdinalIgnoreCase))
            return "Access denied (check API key permissions)";
        if (msg.Contains("404") || msg.Contains("Not Found", StringComparison.OrdinalIgnoreCase))
            return "Endpoint not found (check base URL)";
        if (msg.Contains("429") || msg.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
            return "Rate limited";
        if (msg.Contains("500") || msg.Contains("Internal Server Error", StringComparison.OrdinalIgnoreCase))
            return "Provider server error";
        if (msg.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return "Connection timeout";
        if (msg.Contains("connection", StringComparison.OrdinalIgnoreCase) &&
            msg.Contains("refused", StringComparison.OrdinalIgnoreCase))
            return "Connection refused";

        // Truncate if too long
        return msg.Length > 80 ? msg[..77] + "..." : msg;
    }

    /// <summary>
    /// [Legacy] Validate all LLM providers - throws on failure.
    /// Kept for backward compatibility.
    /// </summary>
    private async Task ValidateProvidersAsync(List<string> providerNames, string executionId)
    {
        var uniqueProviders = providerNames.Distinct().ToList();

        ReportProgress(new MakerProgress
        {
            Phase = MakerPhase.Starting,
            TaskId = executionId,
            Message = $"Validating {uniqueProviders.Count} LLM provider(s): {string.Join(", ", uniqueProviders)}",
            Depth = 0
        });

        var failedProviders = new List<(string Provider, string Error)>();

        foreach (var providerName in uniqueProviders)
        {
            var (isValid, error) = await ValidateSingleProviderAsync(providerName, executionId);
            if (!isValid)
            {
                failedProviders.Add((providerName, error ?? "Unknown error"));
            }
        }

        if (failedProviders.Count > 0)
        {
            var errorDetails = string.Join("\n", failedProviders.Select(f => $"  - {f.Provider}: {f.Error}"));
            var errorMessage = $"LLM provider validation failed:\n{errorDetails}";

            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Failed,
                TaskId = executionId,
                Message = errorMessage,
                Depth = 0
            });

            throw new InvalidOperationException(errorMessage);
        }

        ReportProgress(new MakerProgress
        {
            Phase = MakerPhase.Starting,
            TaskId = executionId,
            Message = $"All {uniqueProviders.Count} LLM provider(s) validated successfully",
            Depth = 0
        });
    }

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