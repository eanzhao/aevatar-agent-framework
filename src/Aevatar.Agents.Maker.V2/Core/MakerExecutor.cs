using System.Diagnostics;

namespace Aevatar.Agents.Maker.V2;

// ============================================================
//  MAKER Executor - Core Orchestration Engine
// ============================================================

/// <summary>
/// Core MAKER executor implementing the full decomposition-voting-composition pipeline.
/// </summary>
public sealed class MakerExecutor : IMakerExecutor
{
    private readonly ExecutionPool _pool;
    private readonly IRedFlagHandler _redFlagHandler;

    /// <summary>
    /// Create a MAKER executor.
    /// </summary>
    /// <param name="pool">LLM execution pool.</param>
    /// <param name="redFlagHandler">Optional custom red flag handler.</param>
    public MakerExecutor(ExecutionPool pool, IRedFlagHandler? redFlagHandler = null)
    {
        _pool = pool;
        _redFlagHandler = redFlagHandler ?? new DefaultRedFlagHandler();
    }

    /// <inheritdoc />
    public async Task<MakerResult> ExecuteAsync(
        string taskDescription,
        MakerOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new MakerOptions();
        var executionId = Guid.NewGuid().ToString("N")[..8];
        var sw = Stopwatch.StartNew();
        var redFlags = new List<RedFlagEvent>();
        var totalLLMCalls = 0;

        // Initialize strategies
        var decomposer = options.Decomposer ?? new DefaultDecomposer();
        var solver = options.Solver ?? new DefaultSolver();
        var composer = options.Composer ?? new DefaultComposer();
        var context = options.Context ?? new Dictionary<string, string>();

        // Report start
        ReportProgress(options, new MakerProgress
        {
            Phase = MakerPhase.Starting,
            TaskId = executionId,
            Message = $"Starting MAKER execution (K={options.ConsensusK}, N={options.SamplesPerRound})",
            Depth = 0
        });

        try
        {
            // Execute the root task
            var (result, node, calls) = await ExecuteTaskAsync(
                taskId: executionId,
                description: taskDescription,
                depth: 0,
                context: new Dictionary<string, string>(context),
                options: options,
                decomposer: decomposer,
                solver: solver,
                composer: composer,
                redFlags: redFlags,
                ct: ct);

            totalLLMCalls = calls;

            sw.Stop();

            ReportProgress(options, new MakerProgress
            {
                Phase = result != null ? MakerPhase.Completed : MakerPhase.Failed,
                TaskId = executionId,
                Message = result != null ? "Execution completed successfully" : "Execution failed",
                Depth = 0
            });

            return new MakerResult
            {
                Success = result != null,
                Content = result ?? string.Empty,
                Error = result == null ? "Task execution failed" : null,
                Trace = new MakerTrace
                {
                    ExecutionId = executionId,
                    RootTask = node,
                    TotalLLMCalls = totalLLMCalls,
                    Duration = sw.Elapsed,
                    RedFlags = redFlags
                }
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new MakerResult
            {
                Success = false,
                Content = string.Empty,
                Error = "Execution cancelled",
                Trace = new MakerTrace
                {
                    ExecutionId = executionId,
                    RootTask = new TaskNode
                    {
                        TaskId = executionId,
                        Description = taskDescription,
                        Depth = 0,
                        IsAtomic = false
                    },
                    TotalLLMCalls = totalLLMCalls,
                    Duration = sw.Elapsed,
                    RedFlags = redFlags
                }
            };
        }
    }

    private async Task<(string? Result, TaskNode Node, int LLMCalls)> ExecuteTaskAsync(
        string taskId,
        string description,
        int depth,
        Dictionary<string, string> context,
        MakerOptions options,
        IDecompositionStrategy decomposer,
        ISolutionStrategy solver,
        ICompositionStrategy composer,
        List<RedFlagEvent> redFlags,
        CancellationToken ct)
    {
        var votingSessions = new List<VotingSession>();
        var children = new List<TaskNode>();
        var totalCalls = 0;
        var recoveryAttempts = 0;

        ReportProgress(options, new MakerProgress
        {
            Phase = MakerPhase.Assessing,
            TaskId = taskId,
            Message = $"Assessing task complexity at depth {depth}",
            Depth = depth
        });

        // Check if task is atomic
        var isAtomic = decomposer.IsAtomic(description, depth, options.MaxDepth);

        if (isAtomic)
        {
            // Solve directly
            var (result, session, calls) = await SolveAtomicAsync(
                taskId, description, context, options, solver, redFlags, recoveryAttempts, ct);

            votingSessions.Add(session);
            totalCalls += calls;

            return (result, new TaskNode
            {
                TaskId = taskId,
                Description = description,
                Depth = depth,
                IsAtomic = true,
                VotingSessions = votingSessions,
                Result = result
            }, totalCalls);
        }

        // Decompose the task
        ReportProgress(options, new MakerProgress
        {
            Phase = MakerPhase.Decomposing,
            TaskId = taskId,
            Message = "Decomposing into subtasks",
            Depth = depth
        });

        var (steps, decompSession, decompCalls) = await DecomposeAsync(
            taskId, description, context, options, decomposer, redFlags, recoveryAttempts, ct);

        votingSessions.Add(decompSession);
        totalCalls += decompCalls;

        // Handle decomposition failure
        if (steps.Count == 0)
        {
            var redFlag = new RedFlagEvent
            {
                TaskId = taskId,
                Reason = "Decomposition produced no valid steps",
                Recovered = false
            };
            redFlags.Add(redFlag);

            // Treat as atomic
            var (atomicResult, atomicSession, atomicCalls) = await SolveAtomicAsync(
                taskId, description, context, options, solver, redFlags, recoveryAttempts + 1, ct);

            votingSessions.Add(atomicSession);
            totalCalls += atomicCalls;
            redFlag = redFlag with { Recovered = atomicResult != null };

            return (atomicResult, new TaskNode
            {
                TaskId = taskId,
                Description = description,
                Depth = depth,
                IsAtomic = true,
                VotingSessions = votingSessions,
                Result = atomicResult
            }, totalCalls);
        }

        // Execute subtasks
        ReportProgress(options, new MakerProgress
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

            var (childResult, childNode, childCalls) = await ExecuteTaskAsync(
                childTaskId,
                stepDescription,
                depth + 1,
                childContext,
                options,
                decomposer,
                solver,
                composer,
                redFlags,
                ct);

            children.Add(childNode);
            totalCalls += childCalls;

            if (childResult != null)
            {
                subtaskResults[stepId] = childResult;
                // Add result to context for next steps
                childContext[$"result_{stepId}"] = childResult;
            }
        }

        // Compose results
        ReportProgress(options, new MakerProgress
        {
            Phase = MakerPhase.Composing,
            TaskId = taskId,
            Message = "Composing subtask results",
            Depth = depth
        });

        var composed = composer.Compose(description, subtaskResults, context);
        if (composed == null)
        {
            // Use LLM synthesis
            var (synthResult, synthSession, synthCalls) = await SynthesizeAsync(
                taskId, description, subtaskResults, context, options, composer, redFlags, recoveryAttempts, ct);

            if (synthSession != null)
            {
                votingSessions.Add(synthSession);
            }
            totalCalls += synthCalls;
            composed = synthResult ?? string.Join("\n\n", subtaskResults.Values);
        }

        return (composed, new TaskNode
        {
            TaskId = taskId,
            Description = description,
            Depth = depth,
            IsAtomic = false,
            VotingSessions = votingSessions,
            Children = children,
            Result = composed
        }, totalCalls);
    }

    private async Task<(IReadOnlyList<(string, string)> Steps, VotingSession Session, int Calls)> DecomposeAsync(
        string taskId,
        string description,
        Dictionary<string, string> context,
        MakerOptions options,
        IDecompositionStrategy decomposer,
        List<RedFlagEvent> redFlags,
        int recoveryAttempts,
        CancellationToken ct)
    {
        var prompt = decomposer.BuildDecompositionPrompt(description, context);
        var engine = new VoteEngine(options.ConsensusK, options.MaxVotingRounds);
        var calls = 0;

        const string systemPrompt = "You are a precise task decomposition agent. Output ONLY valid JSON.";
        const int maxTokens = 1024;
        const float temperature = 0.3f;

        VoteResult? voteResult = null;

        await foreach (var response in _pool.ExecuteParallelAsync(
            systemPrompt, prompt, options.SamplesPerRound, temperature, maxTokens, ct))
        {
            calls++;
            
            // Report each LLM proposal
            ReportProgress(options, new MakerProgress
            {
                Phase = MakerPhase.Voting,
                TaskId = taskId,
                Message = $"Received decomposition proposal #{calls}",
                Depth = 0,
                Proposal = new LLMProposal
                {
                    ProposalId = $"D{calls}",
                    Content = response.Content,
                    Success = response.Success,
                    Error = response.Error
                }
            });
            
            if (!response.Success || string.IsNullOrWhiteSpace(response.Content))
            {
                continue;
            }

            voteResult = engine.SubmitVote(response.Content);

            ReportProgress(options, new MakerProgress
            {
                Phase = MakerPhase.Voting,
                TaskId = taskId,
                Message = "Voting on decomposition plan",
                Depth = 0,
                Voting = engine.GetProgress(VotingType.Decomposition)
            });

            if (voteResult != null)
            {
                break;
            }
        }

        // If no consensus yet, request more samples
        while (voteResult == null && engine.CurrentRound <= options.MaxVotingRounds)
        {
            await foreach (var response in _pool.ExecuteParallelAsync(
                systemPrompt, prompt, options.SamplesPerRound, temperature, maxTokens, ct))
            {
                calls++;
                
                // Report each LLM proposal
                ReportProgress(options, new MakerProgress
                {
                    Phase = MakerPhase.Voting,
                    TaskId = taskId,
                    Message = $"Received decomposition proposal #{calls}",
                    Depth = 0,
                    Proposal = new LLMProposal
                    {
                        ProposalId = $"D{calls}",
                        Content = response.Content,
                        Success = response.Success,
                        Error = response.Error
                    }
                });
                
                if (!response.Success || string.IsNullOrWhiteSpace(response.Content))
                {
                    continue;
                }

                voteResult = engine.SubmitVote(response.Content);
                if (voteResult != null)
                {
                    break;
                }
            }
        }

        // Force result if still no consensus
        voteResult ??= engine.CheckConsensus() ?? new VoteResult
        {
            Success = false,
            WinningContent = string.Empty,
            WinningHash = string.Empty,
            FailureReason = "No proposals received"
        };

        var session = new VotingSession
        {
            Type = VotingType.Decomposition,
            Candidates = voteResult.AllCandidates,
            Winner = voteResult.Success
                ? new VoteCandidate
                {
                    Hash = voteResult.WinningHash,
                    Content = voteResult.WinningContent,
                    Votes = voteResult.LeaderVotes
                }
                : null,
            Rounds = voteResult.Rounds
        };

        if (!voteResult.Success)
        {
            redFlags.Add(new RedFlagEvent
            {
                TaskId = taskId,
                Reason = voteResult.FailureReason ?? "Decomposition voting failed"
            });

            // Try recovery
            if (!string.IsNullOrWhiteSpace(voteResult.WinningContent))
            {
                var steps = decomposer.ParseDecomposition(voteResult.WinningContent);
                return (steps, session, calls);
            }

            return ([], session, calls);
        }

        var parsedSteps = decomposer.ParseDecomposition(voteResult.WinningContent);
        return (parsedSteps, session, calls);
    }

    private async Task<(string? Result, VotingSession Session, int Calls)> SolveAtomicAsync(
        string taskId,
        string description,
        Dictionary<string, string> context,
        MakerOptions options,
        ISolutionStrategy solver,
        List<RedFlagEvent> redFlags,
        int recoveryAttempts,
        CancellationToken ct)
    {
        ReportProgress(options, new MakerProgress
        {
            Phase = MakerPhase.Solving,
            TaskId = taskId,
            Message = "Solving atomic task",
            Depth = 0
        });

        var prompt = solver.BuildSolvePrompt(description, context);
        var engine = new VoteEngine(options.ConsensusK, options.MaxVotingRounds);
        var calls = 0;

        const string systemPrompt = "You are a precise problem solver. Provide clear, direct answers.";
        const int maxTokens = 2048;
        const float temperature = 0.2f;

        VoteResult? voteResult = null;

        await foreach (var response in _pool.ExecuteParallelAsync(
            systemPrompt, prompt, options.SamplesPerRound, temperature, maxTokens, ct))
        {
            calls++;
            
            // Report each LLM proposal
            ReportProgress(options, new MakerProgress
            {
                Phase = MakerPhase.Solving,
                TaskId = taskId,
                Message = $"Received solution proposal #{calls}",
                Depth = 0,
                Proposal = new LLMProposal
                {
                    ProposalId = $"S{calls}",
                    Content = response.Content,
                    Success = response.Success,
                    Error = response.Error
                }
            });
            
            if (!response.Success || string.IsNullOrWhiteSpace(response.Content))
            {
                continue;
            }

            var cleanedContent = solver.ExtractSolution(response.Content);
            voteResult = engine.SubmitVote(cleanedContent);

            ReportProgress(options, new MakerProgress
            {
                Phase = MakerPhase.Voting,
                TaskId = taskId,
                Message = "Voting on solution",
                Depth = 0,
                Voting = engine.GetProgress(VotingType.Solution)
            });

            if (voteResult != null)
            {
                break;
            }
        }

        // Request more samples if needed
        while (voteResult == null && engine.CurrentRound <= options.MaxVotingRounds)
        {
            await foreach (var response in _pool.ExecuteParallelAsync(
                systemPrompt, prompt, options.SamplesPerRound, temperature, maxTokens, ct))
            {
                calls++;
                
                // Report each LLM proposal
                ReportProgress(options, new MakerProgress
                {
                    Phase = MakerPhase.Solving,
                    TaskId = taskId,
                    Message = $"Received solution proposal #{calls}",
                    Depth = 0,
                    Proposal = new LLMProposal
                    {
                        ProposalId = $"S{calls}",
                        Content = response.Content,
                        Success = response.Success,
                        Error = response.Error
                    }
                });
                
                if (!response.Success || string.IsNullOrWhiteSpace(response.Content))
                {
                    continue;
                }

                var cleanedContent = solver.ExtractSolution(response.Content);
                voteResult = engine.SubmitVote(cleanedContent);
                if (voteResult != null)
                {
                    break;
                }
            }
        }

        voteResult ??= engine.CheckConsensus() ?? new VoteResult
        {
            Success = false,
            WinningContent = string.Empty,
            WinningHash = string.Empty,
            FailureReason = "No proposals received"
        };

        var session = new VotingSession
        {
            Type = VotingType.Solution,
            Candidates = voteResult.AllCandidates,
            Winner = voteResult.Success
                ? new VoteCandidate
                {
                    Hash = voteResult.WinningHash,
                    Content = voteResult.WinningContent,
                    Votes = voteResult.LeaderVotes
                }
                : null,
            Rounds = voteResult.Rounds
        };

        if (!voteResult.Success)
        {
            redFlags.Add(new RedFlagEvent
            {
                TaskId = taskId,
                Reason = voteResult.FailureReason ?? "Solution voting failed"
            });

            // Return best effort
            return (
                string.IsNullOrWhiteSpace(voteResult.WinningContent) ? null : voteResult.WinningContent,
                session,
                calls);
        }

        return (voteResult.WinningContent, session, calls);
    }

    private async Task<(string? Result, VotingSession? Session, int Calls)> SynthesizeAsync(
        string taskId,
        string description,
        Dictionary<string, string> subtaskResults,
        Dictionary<string, string> context,
        MakerOptions options,
        ICompositionStrategy composer,
        List<RedFlagEvent> redFlags,
        int recoveryAttempts,
        CancellationToken ct)
    {
        var prompt = composer.BuildSynthesisPrompt(description, subtaskResults, context);
        var calls = 0;

        const string systemPrompt = "You are a synthesis expert. Combine information coherently.";
        const int maxTokens = 4096;
        const float temperature = 0.3f;

        // For synthesis, we use simpler single-shot (no voting, as input is deterministic)
        var response = await _pool.ExecuteAsync(systemPrompt, prompt, temperature, maxTokens, ct);
        calls++;

        if (!response.Success || string.IsNullOrWhiteSpace(response.Content))
        {
            return (null, null, calls);
        }

        return (response.Content.Trim(), null, calls);
    }

    private static void ReportProgress(MakerOptions options, MakerProgress progress)
    {
        options.OnProgress?.Invoke(progress);
    }
}
