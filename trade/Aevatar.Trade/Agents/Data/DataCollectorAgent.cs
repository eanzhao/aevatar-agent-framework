using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Aevatar.Trade.Infrastructure.WeexApi;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Trade.Agents.Data;

/// <summary>
/// 数据采集 Agent
/// 职责：连接 WEEX API，采集市场数据，转换为内部事件广播给下游 Agent
/// </summary>
public class DataCollectorAgent : GAgentBase<DataCollectorState>
{
    // ============ Dependencies ============
    
    private IWeexApiClient? _apiClient;
    private WeexWebSocketClient? _wsClient;
    private readonly List<string> _subscribedSymbols = new();
    private Timer? _heartbeatTimer;

    public IWeexApiClient ApiClient
    {
        set => _apiClient = value;
    }

    public WeexWebSocketClient WebSocketClient
    {
        set
        {
            _wsClient = value;
            SetupWebSocketHandlers();
        }
    }

    // ============ Lifecycle ============

    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        State.AgentId = Id.ToString();
        State.IsConnected = false;
        State.TicksReceived = 0;

        Logger.LogInformation("[DataCollector] Agent activated: {AgentId}", State.AgentId);
    }

    public override async Task OnDeactivateAsync(CancellationToken ct = default)
    {
        _heartbeatTimer?.Dispose();
        
        if (_wsClient != null)
        {
            await _wsClient.DisconnectAsync();
        }

        await base.OnDeactivateAsync(ct);
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult(
            $"DataCollector: {_subscribedSymbols.Count} symbols, " +
            $"{State.TicksReceived} ticks, " +
            $"Connected: {State.IsConnected}");
    }

    // ============ Public Methods ============

    /// <summary>
    /// 启动数据采集
    /// </summary>
    public async Task StartCollectingAsync(
        IEnumerable<string> symbols,
        string klineInterval = "15m",
        CancellationToken ct = default)
    {
        if (_wsClient == null)
            throw new InvalidOperationException("WebSocket client not configured");

        // 连接 WebSocket
        await _wsClient.ConnectAsync(ct);
        State.IsConnected = true;

        // 订阅行情
        foreach (var symbol in symbols)
        {
            await _wsClient.SubscribeTickerAsync(symbol, ct);
            await _wsClient.SubscribeKlineAsync(symbol, klineInterval, ct);
            
            _subscribedSymbols.Add(symbol);
            State.SubscribedSymbols.Add(symbol);
        }

        // 启动心跳
        _heartbeatTimer = new Timer(
            _ => CheckConnection(),
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30));

        Logger.LogInformation(
            "[DataCollector] Started collecting for {Count} symbols: {Symbols}",
            _subscribedSymbols.Count,
            string.Join(", ", _subscribedSymbols));

        // 发布系统启动事件
        await PublishAsync(new SystemStartedEvent
        {
            SystemId = State.AgentId,
            ActiveSymbols = { _subscribedSymbols },
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });
    }

    /// <summary>
    /// 停止数据采集
    /// </summary>
    public async Task StopCollectingAsync(string reason = "Manual stop")
    {
        if (_wsClient != null)
        {
            await _wsClient.DisconnectAsync();
        }

        State.IsConnected = false;
        _heartbeatTimer?.Dispose();

        Logger.LogInformation("[DataCollector] Stopped collecting: {Reason}", reason);

        await PublishAsync(new SystemStoppedEvent
        {
            SystemId = State.AgentId,
            Reason = reason,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });
    }

    /// <summary>
    /// 手动拉取历史K线
    /// </summary>
    public async Task FetchHistoricalKlinesAsync(
        string symbol,
        string interval,
        int limit = 100,
        CancellationToken ct = default)
    {
        if (_apiClient == null)
            throw new InvalidOperationException("API client not configured");

        var klines = await _apiClient.GetKlinesAsync(symbol, interval, limit, ct);

        foreach (var kline in klines)
        {
            await PublishAsync(new KlineUpdateEvent
            {
                Symbol = symbol,
                Interval = interval,
                Open = kline.Open,
                High = kline.High,
                Low = kline.Low,
                Close = kline.Close,
                Volume = (double)kline.Volume,
                OpenTime = Timestamp.FromDateTime(kline.OpenTime),
                CloseTime = Timestamp.FromDateTime(kline.CloseTime)
            });
        }

        Logger.LogDebug(
            "[DataCollector] Fetched {Count} historical klines for {Symbol}",
            klines.Count, symbol);
    }

    // ============ WebSocket Handlers ============

    private void SetupWebSocketHandlers()
    {
        if (_wsClient == null) return;

        _wsClient.OnTicker += OnTickerReceived;
        _wsClient.OnKline += OnKlineReceived;
        _wsClient.OnConnected += OnWebSocketConnected;
        _wsClient.OnDisconnected += OnWebSocketDisconnected;
        _wsClient.OnError += OnWebSocketError;
    }

    private void OnTickerReceived(TickerResponse ticker)
    {
        State.TicksReceived++;
        State.LatestPrices[ticker.Symbol] = ticker.LastPrice;
        State.LastTickTime = Timestamp.FromDateTime(DateTime.UtcNow);

        // 转换为内部事件并广播
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

        // Fire and forget - 不阻塞 WebSocket 接收
        _ = PublishTickerEventAsync(evt);
    }

    private async Task PublishTickerEventAsync(MarketTickEvent evt)
    {
        try
        {
            await PublishAsync(evt);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[DataCollector] Failed to publish ticker event");
        }
    }

    private void OnKlineReceived(KlineData kline)
    {
        var evt = new KlineUpdateEvent
        {
            Symbol = "", // WebSocket kline 需要从 channel 解析
            Interval = "",
            Open = (double)kline.Open,
            High = (double)kline.High,
            Low = (double)kline.Low,
            Close = (double)kline.Close,
            Volume = (double)kline.Volume,
            OpenTime = Timestamp.FromDateTime(kline.OpenTime),
            CloseTime = Timestamp.FromDateTime(kline.CloseTime)
        };

        _ = PublishKlineEventAsync(evt);
    }

    private async Task PublishKlineEventAsync(KlineUpdateEvent evt)
    {
        try
        {
            await PublishAsync(evt);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[DataCollector] Failed to publish kline event");
        }
    }

    private void OnWebSocketConnected()
    {
        State.IsConnected = true;
        Logger.LogInformation("[DataCollector] WebSocket connected");
    }

    private void OnWebSocketDisconnected()
    {
        State.IsConnected = false;
        Logger.LogWarning("[DataCollector] WebSocket disconnected");

        // 尝试重连
        _ = TryReconnectAsync();
    }

    private void OnWebSocketError(string error)
    {
        Logger.LogError("[DataCollector] WebSocket error: {Error}", error);
    }

    // ============ Private Methods ============

    private void CheckConnection()
    {
        if (!State.IsConnected && _wsClient != null)
        {
            Logger.LogWarning("[DataCollector] Connection lost, attempting reconnect...");
            _ = TryReconnectAsync();
        }
    }

    private async Task TryReconnectAsync()
    {
        if (_wsClient == null) return;

        try
        {
            await _wsClient.ConnectAsync();

            // 重新订阅
            foreach (var symbol in _subscribedSymbols)
            {
                await _wsClient.SubscribeTickerAsync(symbol);
            }

            State.IsConnected = true;
            Logger.LogInformation("[DataCollector] Reconnected successfully");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[DataCollector] Reconnection failed");
        }
    }
}
