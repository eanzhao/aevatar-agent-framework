using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.Core.Hierarchy;
using Aevatar.Trade.Agents.Analysts;
using Aevatar.Trade.Agents.Audit;
using Aevatar.Trade.Agents.AiWars;
using Aevatar.Trade.Agents.Coordinator;
using Aevatar.Trade.Agents.Data;
using Aevatar.Trade.Agents.Execution;
using Aevatar.Trade.Agents.RiskControl;
using Aevatar.Trade.Infrastructure.AiWars;
using Aevatar.Trade.Infrastructure.DecisionEngines;
using Aevatar.Trade.Infrastructure.WeexApi;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Trade;

/// <summary>
/// Trading system entry point
/// Responsible for creating and orchestrating all agents
/// </summary>
public class TradingSystem : IAsyncDisposable
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly IWeexApiClient _apiClient;
    private readonly IWeexAiWarsLogClient _aiWarsClient;
    private readonly WeexWebSocketClient _wsClient;
    private readonly TradingConfig _tradingConfig;
    private readonly AnalysisWeightConfig _analysisConfig;
    private readonly RiskControlConfig _riskConfig;
    private readonly TradeAuditConfig _auditConfig;
    private readonly AiWarsLogUploadConfig _aiWarsConfig;
    private readonly DecisionEngineConfig _decisionEngineConfig;
    private readonly CognitiveMeshDecisionEngine _cognitiveMeshDecisionEngine;
    private readonly LLMProvidersConfig _llmProvidersConfig;
    private readonly ILogger<TradingSystem> _logger;

    // Agent Actors
    private IGAgentActor? _dataCollectorActor;
    private IGAgentActor? _sentimentActor;
    private IGAgentActor? _technicalActor;
    private IGAgentActor? _coordinatorActor;
    private IGAgentActor? _riskManagerActor;
    private IGAgentActor? _executorActor;
    private IGAgentActor? _auditActor;
    private IGAgentActor? _aiWarsUploaderActor;

    public TradingSystem(
        IGAgentActorFactory actorFactory,
        IWeexApiClient apiClient,
        IWeexAiWarsLogClient aiWarsClient,
        WeexWebSocketClient wsClient,
        IOptions<TradingConfig> tradingConfig,
        IOptions<AnalysisWeightConfig> analysisConfig,
        IOptions<RiskControlConfig> riskConfig,
        IOptions<TradeAuditConfig> auditConfig,
        IOptions<AiWarsLogUploadConfig> aiWarsConfig,
        IOptions<DecisionEngineConfig> decisionEngineConfig,
        CognitiveMeshDecisionEngine cognitiveMeshDecisionEngine,
        IOptions<LLMProvidersConfig> llmProvidersConfig,
        ILogger<TradingSystem> logger)
    {
        _actorFactory = actorFactory;
        _apiClient = apiClient;
        _aiWarsClient = aiWarsClient;
        _wsClient = wsClient;
        _tradingConfig = tradingConfig.Value;
        _analysisConfig = analysisConfig.Value;
        _riskConfig = riskConfig.Value;
        _auditConfig = auditConfig.Value;
        _aiWarsConfig = aiWarsConfig.Value;
        _decisionEngineConfig = decisionEngineConfig.Value;
        _cognitiveMeshDecisionEngine = cognitiveMeshDecisionEngine;
        _llmProvidersConfig = llmProvidersConfig.Value;
        _logger = logger;
    }

    /// <summary>
    /// Initialize the trading system
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Initializing Trading System...");

        var providerName = string.IsNullOrWhiteSpace(_llmProvidersConfig.Default)
            ? "openai-gpt4"
            : _llmProvidersConfig.Default;

        // ============ Create Agent Actors ============

        // 1. Data collector
        _dataCollectorActor = await _actorFactory.CreateGAgentActorAsync<DataCollectorAgent>(Guid.NewGuid().ToString(), ct);
        var dataCollector = (DataCollectorAgent)_dataCollectorActor.GetAgent();
        dataCollector.ApiClient = _apiClient;
        dataCollector.WebSocketClient = _wsClient;

        // 2. Analysts
        _sentimentActor = await _actorFactory.CreateGAgentActorAsync<MarketSentimentAgent>(Guid.NewGuid().ToString(), ct);
        var sentiment = (MarketSentimentAgent)_sentimentActor.GetAgent();
        sentiment.AllowDangerousTools = false; // safe default
        await sentiment.InitializeAsync(providerName, cancellationToken: ct);
        
        _technicalActor = await _actorFactory.CreateGAgentActorAsync<TechnicalAnalystAgent>(Guid.NewGuid().ToString(), ct);
        var technical = (TechnicalAnalystAgent)_technicalActor.GetAgent();
        technical.AllowDangerousTools = false; // safe default
        await technical.InitializeAsync(providerName, cancellationToken: ct);

        // 3. Coordinator
        _coordinatorActor = await _actorFactory.CreateGAgentActorAsync<TradingCoordinatorAgent>(Guid.NewGuid().ToString(), ct);
        var coordinator = (TradingCoordinatorAgent)_coordinatorActor.GetAgent();
        coordinator.AllowDangerousTools = false; // coordinator should not directly trade
        await coordinator.InitializeAsync(providerName, cancellationToken: ct);
        coordinator.Configure(
            _tradingConfig.MinConfidenceToTrade,
            _analysisConfig.SentimentWeight,
            _analysisConfig.TechnicalWeight,
            _analysisConfig.NewsWeight,
            _tradingConfig.ExecutionMode);
        
        if (string.Equals(_decisionEngineConfig.Mode, "CognitiveMesh", StringComparison.OrdinalIgnoreCase))
        {
            coordinator.DecisionEngine = _cognitiveMeshDecisionEngine;
            _logger.LogInformation("Coordinator decision engine: CognitiveMesh");
        }
        else
        {
            coordinator.DecisionEngine = null; // Fallback to direct LLM
            _logger.LogInformation("Coordinator decision engine: Direct");
        }

        // 4. Risk manager
        _riskManagerActor = await _actorFactory.CreateGAgentActorAsync<RiskManagerAgent>(Guid.NewGuid().ToString(), ct);
        var riskManager = (RiskManagerAgent)_riskManagerActor.GetAgent();
        // Allow dangerous tools (order placement/cancel) only in Live mode.
        riskManager.AllowDangerousTools = _tradingConfig.ExecutionMode == TradeExecutionMode.Live;
        await riskManager.InitializeAsync(providerName, cancellationToken: ct);
        riskManager.Configure(
            _tradingConfig.MaxPositionPct,
            _tradingConfig.MaxTotalPositionPct,
            _tradingConfig.MaxLossPerTrade,
            _tradingConfig.MaxDailyLoss,
            _riskConfig.MaxConsecutiveLosses,
            _riskConfig.CooldownMinutes);

        // 5. Executor
        _executorActor = await _actorFactory.CreateGAgentActorAsync<ExecutorAgent>(Guid.NewGuid().ToString(), ct);
        var executor = (ExecutorAgent)_executorActor.GetAgent();
        executor.Configure(_tradingConfig.ExecutionMode);
        executor.ApiClient = _apiClient;
        
        // 6. Trade audit (optional)
        if (_auditConfig.Enabled)
        {
            _auditActor = await _actorFactory.CreateGAgentActorAsync<TradeAuditAgent>(Guid.NewGuid().ToString(), ct);
            var audit = (TradeAuditAgent)_auditActor.GetAgent();
            audit.Configure(_auditConfig);
        }
        
        // 7. AI Wars uploader (optional)
        if (_auditActor != null && (_aiWarsConfig.Enabled || _auditConfig.RequestAiwarsUpload))
        {
            _aiWarsUploaderActor = await _actorFactory.CreateGAgentActorAsync<AiWarsLogUploaderAgent>(Guid.NewGuid().ToString(), ct);
            var uploader = (AiWarsLogUploaderAgent)_aiWarsUploaderActor.GetAgent();
            uploader.Client = _aiWarsClient;
        }

        // ============ Establish Hierarchy ============
        // 
        // DataCollector (Data Source)
        //      │
        //      ├── SentimentAgent (Analyst)
        //      ├── TechnicalAgent (Analyst)
        //      │
        //      └── Coordinator (Decision Maker)
        //              │
        //              └── RiskManager (Risk Control)
        //                      │
        //                      └── Executor (Execution)

        await ActorHierarchyCoordinator.LinkAsync(_dataCollectorActor, _sentimentActor, _logger, ct);
        await ActorHierarchyCoordinator.LinkAsync(_dataCollectorActor, _technicalActor, _logger, ct);
        await ActorHierarchyCoordinator.LinkAsync(_dataCollectorActor, _coordinatorActor, _logger, ct);
        await ActorHierarchyCoordinator.LinkAsync(_coordinatorActor, _riskManagerActor, _logger, ct);
        await ActorHierarchyCoordinator.LinkAsync(_riskManagerActor, _executorActor, _logger, ct);
        
        // Attach audit agent to key nodes (decision/risk/execution) for event capture
        if (_auditActor != null)
        {
            await ActorHierarchyCoordinator.LinkAsync(_coordinatorActor, _auditActor, _logger, ct);
            await ActorHierarchyCoordinator.LinkAsync(_riskManagerActor, _auditActor, _logger, ct);
            await ActorHierarchyCoordinator.LinkAsync(_executorActor, _auditActor, _logger, ct);
        }
        
        // Audit -> Uploader (audit publishes AiWarsLogUploadRequestedEvent downward)
        if (_auditActor != null && _aiWarsUploaderActor != null)
        {
            await ActorHierarchyCoordinator.LinkAsync(_auditActor, _aiWarsUploaderActor, _logger, ct);
        }

        _logger.LogInformation("Trading System initialized with Agent hierarchy");
    }

    /// <summary>
    /// Start the trading system
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting Trading System for {Symbol}...", _tradingConfig.Symbol);

        // Sync account information
        await SyncAccountInfoAsync();

        // Start data collection
        var dataCollector = (DataCollectorAgent)_dataCollectorActor!.GetAgent();
        await dataCollector.StartCollectingAsync(
            new[] { _tradingConfig.Symbol },
            _tradingConfig.Interval,
            ct);

        // Fetch historical kline data (for technical analysis initialization)
        await dataCollector.FetchHistoricalKlinesAsync(
            _tradingConfig.Symbol,
            _tradingConfig.Interval,
            200,
            ct);

        _logger.LogInformation("Trading System started");
    }

    /// <summary>
    /// Stop the trading system
    /// </summary>
    public async Task StopAsync(string reason = "Manual stop")
    {
        _logger.LogInformation("Stopping Trading System: {Reason}", reason);

        var dataCollector = (DataCollectorAgent)_dataCollectorActor!.GetAgent();
        await dataCollector.StopCollectingAsync(reason);

        _logger.LogInformation("Trading System stopped");
    }

    /// <summary>
    /// Sync account information
    /// </summary>
    public async Task SyncAccountInfoAsync()
    {
        try
        {
            var balances = await _apiClient.GetBalancesAsync();
            var usdtBalance = balances.FirstOrDefault(b => b.Currency == "USDT");
            
            if (usdtBalance != null)
            {
                var riskManager = (RiskManagerAgent)_riskManagerActor!.GetAgent();
                riskManager.UpdateAccountInfo(
                    totalEquity: (double)usdtBalance.Balance,
                    availableBalance: (double)usdtBalance.Available,
                    currentPositionValue: 0, // Simplified handling
                    unrealizedPnl: 0);

                _logger.LogInformation(
                    "Account synced: Balance=${Balance}, Available=${Available}",
                    usdtBalance.Balance, usdtBalance.Available);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync account info");
        }
    }

    /// <summary>
    /// Get system status
    /// </summary>
    public async Task<TradingSystemStatus> GetStatusAsync()
    {
        return new TradingSystemStatus
        {
            DataCollector = await _dataCollectorActor!.GetDescriptionAsync(),
            SentimentAnalyst = await _sentimentActor!.GetDescriptionAsync(),
            TechnicalAnalyst = await _technicalActor!.GetDescriptionAsync(),
            Coordinator = await _coordinatorActor!.GetDescriptionAsync(),
            RiskManager = await _riskManagerActor!.GetDescriptionAsync(),
            Executor = await _executorActor!.GetDescriptionAsync(),
            TradeAudit = _auditActor != null ? await _auditActor.GetDescriptionAsync() : "TradeAudit: disabled",
            AiWarsUploader = _aiWarsUploaderActor != null ? await _aiWarsUploaderActor.GetDescriptionAsync() : "AiWarsUploader: disabled"
        };
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync("System disposing");
        await _wsClient.DisposeAsync();
    }
}

/// <summary>
/// System status
/// </summary>
public record TradingSystemStatus
{
    public required string DataCollector { get; init; }
    public required string SentimentAnalyst { get; init; }
    public required string TechnicalAnalyst { get; init; }
    public required string Coordinator { get; init; }
    public required string RiskManager { get; init; }
    public required string Executor { get; init; }
    public required string TradeAudit { get; init; }
    public required string AiWarsUploader { get; init; }
}
