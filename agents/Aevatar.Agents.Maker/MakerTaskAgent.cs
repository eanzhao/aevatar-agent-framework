using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aevatar.Agents.AI.Core;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Maker;

/// <summary>
/// MAKER task agent orchestrates decomposition, voting, and recursion flows.
/// </summary>
public partial class MakerTaskAgent : AIGAgentBase<TaskAgentState, TaskAgentConfig>
{
    private const int VotePreviewLength = 180;
    private const int MicroStageRetryLimit = 2;

    private readonly ConcurrentDictionary<string, byte> _selfHandledRequests = new();
    private readonly SemaphoreSlim _selfWorkerInitLock = new(1, 1);
    private readonly SemaphoreSlim _voteLock = new(1, 1);
    private readonly IMakerChildLinker? _childLinker;
    private readonly Dictionary<string, string> _currentContextSnapshot = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, int> _microStageRetryCounters = new();
    private readonly HashSet<string> _microSummaryFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonDocumentOptions _jsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private bool _selfWorkerInitialized;
    private bool _childLinkerWarningLogged;

    public MakerTaskAgent() : this(null)
    {
    }

    public MakerTaskAgent(IMakerChildLinker? childLinker)
    {
        _childLinker = childLinker;
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        InitializeStateDefaults();
        InitializeConfigDefaults();

        if (LLMProviderFactory != null)
        {
            try
            {
                var providerName = CustomConfig.ProviderName;
                if (!string.IsNullOrWhiteSpace(providerName))
                {
                    var providerConfig = LLMProviderFactory.GetProviderConfig(providerName);
                    if (providerConfig?.Embeddings?.Enabled == true)
                    {
                        await InitializeEmbeddingGeneratorAsync(providerConfig, ct);
                    }
                    else
                    {
                        var defaultProviderConfig = LLMProviderFactory.GetDefaultProviderConfig();
                        if (defaultProviderConfig?.Embeddings?.Enabled == true)
                        {
                            await InitializeEmbeddingGeneratorAsync(defaultProviderConfig, ct);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Failed to initialize embedding generator: {Message}", ex.Message);
            }
        }

        SystemPrompt = CustomConfig.SystemPromptTemplate;
        CustomState.LastUpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
    }

    private void InitializeStateDefaults()
    {
        if (string.IsNullOrWhiteSpace(CustomState.TaskId))
        {
            CustomState.TaskId = Id.ToString();
        }

        if (CustomState.CurrentDepth < 0)
        {
            CustomState.CurrentDepth = 0;
        }

        if (!System.Enum.IsDefined(typeof(TaskAgentState.Types.Phase), CustomState.Phase))
        {
            CustomState.Phase = TaskAgentState.Types.Phase.Created;
        }

        if (!System.Enum.IsDefined(typeof(TaskAgentState.Types.GenerationRequestType), CustomState.ActiveGenerationType))
        {
            CustomState.ActiveGenerationType = TaskAgentState.Types.GenerationRequestType.None;
        }
    }

    private void InitializeConfigDefaults()
    {
        if (CustomConfig.ConsensusThresholdK <= 0)
        {
            CustomConfig.ConsensusThresholdK = 2;
        }

        if (CustomConfig.MaxDepth <= 0)
        {
            CustomConfig.MaxDepth = 4;
        }

        if (CustomConfig.MaxAttempts <= 0)
        {
            CustomConfig.MaxAttempts = 3;
        }

        if (CustomConfig.InitialFanOut <= 0)
        {
            CustomConfig.InitialFanOut = 3;
        }

        if (CustomConfig.MaxCandidateWait <= 0)
        {
            CustomConfig.MaxCandidateWait = 12;
        }

        if (string.IsNullOrWhiteSpace(CustomConfig.ProviderName))
        {
            CustomConfig.ProviderName = "deepseek";
        }

        if (string.IsNullOrWhiteSpace(CustomConfig.SystemPromptTemplate))
        {
            CustomConfig.SystemPromptTemplate = """
You are a MAKER supervisor. Your job is to orchestrate recursive decomposition and ensure zero-error execution.
- Always reason about confidence.
- Prefer decomposition until tasks are clearly atomic.
- Track context variables and prepare child assignments.
""";
        }

        if (CustomConfig.WorkerResponseTokenLimit <= 0)
        {
            CustomConfig.WorkerResponseTokenLimit = 160;
        }

        if (CustomConfig.WorkerStopSequences.Count == 0)
        {
            CustomConfig.WorkerStopSequences.Add("<END>");
        }

        if (CustomConfig.ChildPoolSize <= 0)
        {
            CustomConfig.ChildPoolSize = 2;
        }

        if (CustomConfig.SemanticSimilarityThreshold <= 0)
        {
            CustomConfig.SemanticSimilarityThreshold = 0.95f;
        }
    }

    public override Task<string> GetDescriptionAsync()
    {
        var phase = CustomState.Phase.ToString();
        return Task.FromResult($"MAKER Task ({phase}) - depth {CustomState.CurrentDepth}");
    }

    public void EnableExternalConsensus()
    {
        CustomConfig.UseConsensusAgent = true;
    }

    public bool IsExternalConsensusEnabled => CustomConfig.UseConsensusAgent;

    public string? ProviderName => CustomConfig.ProviderName;
}
