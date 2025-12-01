using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.CreativeReasoning.Agents;
using Aevatar.Agents.CreativeReasoning.Core;
using Aevatar.Agents.CreativeReasoning.Messages;
using Aevatar.Agents.CreativeReasoning.Strategies;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.CreativeReasoning.Execution;

// ============================================================
//  UoT Executor - Agent-based Creative Reasoning Entry Point
//  Uses IGAgentActorFactory for proper actor lifecycle management
// ============================================================

/// <summary>
/// Executor for Universe of Thoughts (UoT) creative reasoning.
/// Uses Aevatar Agent Actor Framework for proper event-driven execution.
/// </summary>
public class UoTExecutor : IUoTExecutor
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly ILogger<UoTExecutor> _logger;
    private readonly string _defaultProviderName;

    public UoTExecutor(
        IGAgentActorFactory actorFactory,
        ILogger<UoTExecutor> logger,
        string? defaultProviderName = null)
    {
        _actorFactory = actorFactory;
        _logger = logger;
        _defaultProviderName = defaultProviderName ?? "deepseek";
    }

    /// <inheritdoc />
    public async Task<UoTResult> ExecuteAsync(
        string problem,
        UoTOptions options,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(problem))
            throw new ArgumentException("Problem cannot be empty", nameof(problem));

        var providerName = options.ProviderName ?? _defaultProviderName;
        var executionId = Guid.NewGuid();
        var executionIdShort = executionId.ToString("N")[..8];

        _logger.LogInformation(
            "Starting C-UoT execution {ExecutionId} for problem: {Problem}",
            executionIdShort, problem[..Math.Min(50, problem.Length)]);

        IGAgentActor? coordinatorActor = null;

        try
        {
            // Step 1: Create Coordinator Actor via ActorFactory
            coordinatorActor = await _actorFactory.CreateGAgentActorAsync<UoTCoordinatorGAgent>(executionId, ct);
            _logger.LogDebug("Created UoT coordinator actor {CoordinatorId}", executionId);

            // Step 2: Get underlying Agent and inject strategies
            var coordinator = coordinatorActor.GetAgent() as UoTCoordinatorGAgent;
            if (coordinator == null)
            {
                throw new InvalidOperationException("Failed to get UoTCoordinatorGAgent from actor");
            }

            // Inject strategies and progress callback
            coordinator.SetStrategies(
                analogyStrategy: new DefaultAnalogyStrategy(),
                decompositionStrategy: new DefaultThoughtDecompositionStrategy(),
                hostSelectionStrategy: new DefaultHostSelectionStrategy(),
                donorSelectionStrategy: new DefaultDonorSelectionStrategy(),
                synthesisStrategy: new DefaultSynthesisStrategy(),
                evaluationStrategy: new DefaultEvaluationStrategy(),
                progressCallback: options.OnProgress);

            // Step 3: Build and send start request
            var startRequest = new StartCreativeReasoningRequest
            {
                ExecutionId = executionIdShort,
                Problem = problem,
                DomainHint = options.DomainHint ?? "",
                ProviderName = providerName,
                MaxAnalogies = options.MaxAnalogies,
                MaxCandidates = options.MaxCandidates,
                FeasibilityThreshold = options.FeasibilityThreshold,
                UtilityWeight = options.UtilityWeight,
                NoveltyWeight = options.NoveltyWeight
            };

            // Send event to Actor (triggers EventHandler)
            var envelope = new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = TimestampHelper.GetUtcNow(),
                Payload = Any.Pack(startRequest),
                Direction = EventDirection.Down
            };
            await coordinatorActor.HandleEventAsync(envelope, ct);

            _logger.LogDebug("Sent StartCreativeReasoningRequest to coordinator");

            // Step 4: Poll for completion
            var maxWaitTime = TimeSpan.FromMinutes(10);
            var pollInterval = TimeSpan.FromMilliseconds(300);
            var startTime = DateTime.UtcNow;

            while (DateTime.UtcNow - startTime < maxWaitTime)
            {
                ct.ThrowIfCancellationRequested();

                await Task.Delay(pollInterval, ct);

                var phase = coordinator.GetPhase();
                if (phase == UoTExecutionPhase.UotPhaseCompleted || phase == UoTExecutionPhase.UotPhaseFailed)
                {
                    var result = coordinator.GetResult();

                    _logger.LogInformation(
                        "C-UoT execution {ExecutionId} completed: Success={Success}, Candidates={Count}, LLMCalls={Calls}",
                        executionIdShort, result.Success, result.AllCandidates.Count, result.Trace.TotalLLMCalls);

                    return result;
                }
            }

            // Timeout
            _logger.LogWarning("C-UoT execution {ExecutionId} timed out", executionIdShort);

            return new UoTResult
            {
                Success = false,
                Error = "Execution timed out",
                BestSolution = null,
                AllCandidates = [],
                Trace = new UoTResultTrace
                {
                    ExecutionId = executionIdShort,
                    OriginalProblem = problem,
                    TotalLLMCalls = coordinator.GetResult().Trace.TotalLLMCalls,
                    TotalTokens = coordinator.GetResult().Trace.TotalTokens,
                    Duration = DateTime.UtcNow - startTime
                }
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("C-UoT execution {ExecutionId} was cancelled", executionIdShort);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "C-UoT execution {ExecutionId} failed", executionIdShort);

            return new UoTResult
            {
                Success = false,
                Error = ex.Message,
                BestSolution = null,
                AllCandidates = [],
                Trace = new UoTResultTrace
                {
                    ExecutionId = executionIdShort,
                    OriginalProblem = problem,
                    TotalLLMCalls = 0,
                    TotalTokens = 0,
                    Duration = TimeSpan.Zero
                }
            };
        }
    }
}

