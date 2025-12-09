using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Aevatar.Trade.Infrastructure.WeexApi;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Trade.Agents;

/// <summary>
/// 数据采集 Agent
/// 职责：统一数据采集入口，将外部数据转换为内部事件
/// 
/// 数据流：
/// [WEEX WebSocket] → DataCollectorAgent → [MarketTickEvent/KlineUpdateEvent] → 子 Agent
/// </summary>
public class DataCollectorAgent : GAgentBase<DataCollectorState>
{
    private readonly IWeexApiClient _apiClient;
    private readonly WeexWebSocketClient _wsClient;
    private readonly List<string> _subscribedSymbols = new();

    public DataCollectorAgent(
        IWeexApiClient apiClient,
        WeexWebSocketClient wsClient)
    {
        _apiClient = apiClient;
        _wsClient = wsClient;
    }

    public override Task<string> GetDescriptionAsync()
        => Task.FromResult($"数据采集器 - 已订阅 {State.SubscribedSymbols.Count} 个交易对");

    // ============ Lifecycle ============

    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        State.AgentId = Id.ToString();
        State.IsConnected = false;

        // 注册 WebSocket 事件处理
        _wsClient.OnTicker += OnTickerReceived;
        _wsClient.OnKline += OnKlineReceived;
        _wsClient.OnConnected += () =>
        {
            State.IsConnected = true;
            Logger.LogInformation("WebSocket connected");
        };
        _wsClient.OnDisconnected += () =>
        {
            State.IsConnected = false;
            Logger.LogWarning("WebSocket disconnected");
        };
        _wsClient.OnError += error =>
        {
            Logger.LogError("WebSocket error: {Error}", error);
        };
    }

    public override async Task OnDeactivateAsync(CancellationToken ct = default)
    {
        // 断开 WebSocket
        await _wsClient.DisconnectAsync();

        _wsClient.OnTicker -= OnTickerReceived;
        _wsClient.OnKline -= OnKlineReceived;

        await base.OnDeactivateAsync(ct);
    }

    // ============ Public Methods ============

    /// <summary>
    /// 启动数据采集
    /// </summary>
    public async Task StartAsync(IEnumerable<string> symbols, CancellationToken ct = default)
    {
        // 连接 WebSocket
        await _wsClient.ConnectAsync(ct);

        // 订阅行情
        foreach (var symbol in symbols)
        {
            await SubscribeSymbolAsync(symbol, ct);
        }

        // 发布系统启动事件
        await PublishAsync(new SystemStartedEvent
        {
            SystemId = State.AgentId,
            ActiveSymbols = { _subscribedSymbols },
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        }, EventDirection.Down);

        Logger.LogInformation("DataCollector started with {Count} symbols", _subscribedSymbols.Count);
    }

    /// <summary>
    /// 停止数据采集
    /// </summary>
    public async Task StopAsync(string reason = "Manual stop")
    {
        await _wsClient.DisconnectAsync();

        await PublishAsync(new SystemStoppedEvent
        {
            SystemId = State.AgentId,
            Reason = reason,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        }, EventDirection.Down);

        Logger.LogInformation("DataCollector stopped: {Reason}", reason);
    }

    /// <summary>
    /// 订阅交易对
    /// </summary>
    public async Task SubscribeSymbolAsync(string symbol, CancellationToken ct = default)
    {
        if (_subscribedSymbols.Contains(symbol)) return;

        // 订阅实时行情
        await _wsClient.SubscribeTickerAsync(symbol, ct);

        // 订阅 K 线 (15分钟)
        await _wsClient.SubscribeKlineAsync(symbol, "15m", ct);

        _subscribedSymbols.Add(symbol);
        State.SubscribedSymbols.Add(symbol);

        Logger.LogDebug("Subscribed to {Symbol}", symbol);
    }

    /// <summary>
    /// 获取历史 K 线数据
    /// </summary>
    public async Task<IReadOnlyList<KlineData>> GetHistoricalKlinesAsync(
        string symbol,
        string interval,
        int limit = 100,
        CancellationToken ct = default)
    {
        return await _apiClient.GetKlinesAsync(symbol, interval, limit, ct);
    }

    // ============ WebSocket Event Handlers ============

    private void OnTickerReceived(TickerResponse ticker)
    {
        State.TicksReceived++;
        State.LatestPrices[ticker.Symbol] = ticker.LastPrice;
        State.LastTickTime = Timestamp.FromDateTime(DateTime.UtcNow);

        // 转换并广播事件
        var evt = new MarketTickEvent
        {
            Symbol = ticker.Symbol,
            Price = (double)ticker.LastPrice,
            Bid = (double)ticker.BidPrice,
            Ask = (double)ticker.AskPrice,
            Volume24H = (double)ticker.Volume24h,
            Change24H = (double)ticker.Change24h,
            High24H = (double)ticker.High24h,
            Low24H = (double)ticker.Low24h,
            Timestamp = Timestamp.FromDateTime(ticker.Timestamp)
        };

        // 异步发布，不阻塞接收循环
        _ = Task.Run(async () =>
        {
            try
            {
                await PublishAsync(evt, EventDirection.Down);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to publish MarketTickEvent");
            }
        });
    }

    private void OnKlineReceived(KlineData kline)
    {
        // 转换并广播事件
        var evt = new KlineUpdateEvent
        {
            Symbol = "", // WebSocket 推送中需要从 channel 解析
            Interval = "15m",
            Open = (double)kline.Open,
            High = (double)kline.High,
            Low = (double)kline.Low,
            Close = (double)kline.Close,
            Volume = (double)kline.Volume,
            OpenTime = Timestamp.FromDateTime(kline.OpenTime),
            CloseTime = Timestamp.FromDateTime(kline.CloseTime)
        };

        _ = Task.Run(async () =>
        {
            try
            {
                await PublishAsync(evt, EventDirection.Down);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to publish KlineUpdateEvent");
            }
        });
    }

    // ============ Event Handlers ============

    /// <summary>
    /// 处理新闻数据 (从外部新闻服务接收)
    /// </summary>
    [EventHandler]
    public async Task HandleNewsData(NewsDataEvent evt)
    {
        Logger.LogDebug("Received news: {Headline}", evt.Headline);

        // 转发给子 Agent
        await PublishAsync(evt, EventDirection.Down);
    }

    /// <summary>
    /// 处理社交媒体数据
    /// </summary>
    [EventHandler]
    public async Task HandleSocialData(SocialDataEvent evt)
    {
        Logger.LogDebug("Received social data from {Platform}", evt.Platform);

        // 转发给子 Agent
        await PublishAsync(evt, EventDirection.Down);
    }
}
