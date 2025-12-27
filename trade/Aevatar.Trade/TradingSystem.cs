using Aevatar.Agents.Abstractions;
using Aevatar.Trade.Agents.Analysts;
using Aevatar.Trade.Agents.Coordinator;
using Aevatar.Trade.Agents.Data;
using Aevatar.Trade.Agents.Execution;
using Aevatar.Trade.Agents.RiskControl;
using Aevatar.Trade.Infrastructure.WeexApi;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Trade;

/// <summary>
/// 交易系统入口
/// 负责创建和编排所有 Agent
/// </summary>
public class TradingSystem : IAsyncDisposable
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly IWeexApiClient _apiClient;
    private readonly WeexWebSocketClient _wsClient;
    private readonly TradingConfig _tradingConfig;
    private readonly AnalysisConfig _analysisConfig;
    private readonly RiskControlConfig _riskConfig;
    private readonly ILogger<TradingSystem> _logger;

    // Agent Actors
    private IGAgentActor? _dataCollectorActor;
    private IGAgentActor? _sentimentActor;
    private IGAgentActor? _technicalActor;
    private IGAgentActor? _coordinatorActor;
    private IGAgentActor? _riskManagerActor;
    private IGAgentActor? _executorActor;

    public TradingSystem(
        IGAgentActorFactory actorFactory,
        IWeexApiClient apiClient,
        WeexWebSocketClient wsClient,
        IOptions<TradingConfig> tradingConfig,
        IOptions<AnalysisConfig> analysisConfig,
        IOptions<RiskControlConfig> riskConfig,
        ILogger<TradingSystem> logger)
    {
        _actorFactory = actorFactory;
        _apiClient = apiClient;
        _wsClient = wsClient;
        _tradingConfig = tradingConfig.Value;
        _analysisConfig = analysisConfig.Value;
        _riskConfig = riskConfig.Value;
        _logger = logger;
    }

    /// <summary>
    /// 初始化交易系统
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Initializing Trading System...");

        // ============ 创建 Agent Actors ============

        // 1. 数据采集器
        _dataCollectorActor = await _actorFactory.CreateAsync<DataCollectorAgent>();
        var dataCollector = (DataCollectorAgent)_dataCollectorActor.Agent;
        dataCollector.ApiClient = _apiClient;
        dataCollector.WebSocketClient = _wsClient;

        // 2. 分析师
        _sentimentActor = await _actorFactory.CreateAsync<MarketSentimentAgent>();
        _technicalActor = await _actorFactory.CreateAsync<TechnicalAnalystAgent>();

        // 3. 协调者
        _coordinatorActor = await _actorFactory.CreateAsync<TradingCoordinatorAgent>();
        var coordinator = (TradingCoordinatorAgent)_coordinatorActor.Agent;
        coordinator.Configure(
            _tradingConfig.MinConfidenceToTrade,
            _analysisConfig.SentimentWeight,
            _analysisConfig.TechnicalWeight,
            _analysisConfig.NewsWeight);

        // 4. 风控经理
        _riskManagerActor = await _actorFactory.CreateAsync<RiskManagerAgent>();
        var riskManager = (RiskManagerAgent)_riskManagerActor.Agent;
        riskManager.Configure(
            _tradingConfig.MaxPositionPct,
            _tradingConfig.MaxTotalPositionPct,
            _tradingConfig.MaxLossPerTrade,
            _tradingConfig.MaxDailyLoss,
            _riskConfig.MaxConsecutiveLosses,
            _riskConfig.CooldownMinutes);

        // 5. 执行器
        _executorActor = await _actorFactory.CreateAsync<ExecutorAgent>();
        var executor = (ExecutorAgent)_executorActor.Agent;
        executor.ApiClient = _apiClient;

        // ============ 建立层级关系 ============
        // 
        // DataCollector (数据源)
        //      │
        //      ├── SentimentAgent (分析师)
        //      ├── TechnicalAgent (分析师)
        //      │
        //      └── Coordinator (决策者)
        //              │
        //              └── RiskManager (风控)
        //                      │
        //                      └── Executor (执行)

        // 分析师订阅数据采集器
        await _sentimentActor.SetParentAsync(_dataCollectorActor.Id);
        await _dataCollectorActor.AddChildAsync(_sentimentActor.Id);

        await _technicalActor.SetParentAsync(_dataCollectorActor.Id);
        await _dataCollectorActor.AddChildAsync(_technicalActor.Id);

        // 协调者订阅分析师 (通过数据采集器的广播)
        await _coordinatorActor.SetParentAsync(_dataCollectorActor.Id);
        await _dataCollectorActor.AddChildAsync(_coordinatorActor.Id);

        // 风控订阅协调者
        await _riskManagerActor.SetParentAsync(_coordinatorActor.Id);
        await _coordinatorActor.AddChildAsync(_riskManagerActor.Id);

        // 执行器订阅风控
        await _executorActor.SetParentAsync(_riskManagerActor.Id);
        await _riskManagerActor.AddChildAsync(_executorActor.Id);

        _logger.LogInformation("Trading System initialized with Agent hierarchy");
    }

    /// <summary>
    /// 启动交易系统
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting Trading System for {Symbol}...", _tradingConfig.Symbol);

        // 同步账户信息
        await SyncAccountInfoAsync();

        // 启动数据采集
        var dataCollector = (DataCollectorAgent)_dataCollectorActor!.Agent;
        await dataCollector.StartCollectingAsync(
            new[] { _tradingConfig.Symbol },
            _tradingConfig.Interval,
            ct);

        // 拉取历史K线数据（用于技术分析初始化）
        await dataCollector.FetchHistoricalKlinesAsync(
            _tradingConfig.Symbol,
            _tradingConfig.Interval,
            200,
            ct);

        _logger.LogInformation("Trading System started");
    }

    /// <summary>
    /// 停止交易系统
    /// </summary>
    public async Task StopAsync(string reason = "Manual stop")
    {
        _logger.LogInformation("Stopping Trading System: {Reason}", reason);

        var dataCollector = (DataCollectorAgent)_dataCollectorActor!.Agent;
        await dataCollector.StopCollectingAsync(reason);

        _logger.LogInformation("Trading System stopped");
    }

    /// <summary>
    /// 同步账户信息
    /// </summary>
    public async Task SyncAccountInfoAsync()
    {
        try
        {
            var balances = await _apiClient.GetBalancesAsync();
            var usdtBalance = balances.FirstOrDefault(b => b.Currency == "USDT");
            
            if (usdtBalance != null)
            {
                var riskManager = (RiskManagerAgent)_riskManagerActor!.Agent;
                riskManager.UpdateAccountInfo(
                    totalEquity: (double)usdtBalance.Balance,
                    availableBalance: (double)usdtBalance.Available,
                    currentPositionValue: 0, // 简化处理
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
    /// 获取系统状态
    /// </summary>
    public async Task<TradingSystemStatus> GetStatusAsync()
    {
        return new TradingSystemStatus
        {
            DataCollector = await _dataCollectorActor!.Agent.GetDescriptionAsync(),
            SentimentAnalyst = await _sentimentActor!.Agent.GetDescriptionAsync(),
            TechnicalAnalyst = await _technicalActor!.Agent.GetDescriptionAsync(),
            Coordinator = await _coordinatorActor!.Agent.GetDescriptionAsync(),
            RiskManager = await _riskManagerActor!.Agent.GetDescriptionAsync(),
            Executor = await _executorActor!.Agent.GetDescriptionAsync()
        };
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync("System disposing");
        await _wsClient.DisposeAsync();
    }
}

/// <summary>
/// 系统状态
/// </summary>
public record TradingSystemStatus
{
    public required string DataCollector { get; init; }
    public required string SentimentAnalyst { get; init; }
    public required string TechnicalAnalyst { get; init; }
    public required string Coordinator { get; init; }
    public required string RiskManager { get; init; }
    public required string Executor { get; init; }
}
