namespace Aevatar.Trade.Infrastructure.WeexApi;

/// <summary>
/// WEEX API 客户端接口
/// 基于官方文档: https://www.weex.com/api-doc/spot/introduction/APIBriefIntroduction
/// </summary>
public interface IWeexApiClient
{
    // ============ Market Data ============
    
    /// <summary>
    /// 获取单个交易对行情
    /// </summary>
    Task<TickerResponse> GetTickerAsync(string symbol, CancellationToken ct = default);
    
    /// <summary>
    /// 获取K线数据
    /// </summary>
    /// <param name="symbol">交易对 (e.g., "BTCUSDT_SPBL")</param>
    /// <param name="interval">时间周期 (1m, 5m, 15m, 1h, 4h, 1d)</param>
    /// <param name="limit">数量限制</param>
    Task<IReadOnlyList<KlineData>> GetKlinesAsync(
        string symbol, 
        string interval, 
        int limit = 100, 
        CancellationToken ct = default);
    
    // ============ Account ============
    
    /// <summary>
    /// 获取账户余额
    /// GET /api/v2/account/balance
    /// </summary>
    Task<IReadOnlyList<BalanceInfo>> GetBalancesAsync(CancellationToken ct = default);
    
    /// <summary>
    /// 获取指定币种余额
    /// </summary>
    Task<BalanceInfo?> GetBalanceAsync(string currency, CancellationToken ct = default);
    
    // ============ Trading ============
    
    /// <summary>
    /// 下单
    /// POST /api/v2/trade/orders
    /// </summary>
    Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default);
    
    /// <summary>
    /// 取消订单
    /// POST /api/v2/trade/cancel-order
    /// </summary>
    Task<CancelOrderResult> CancelOrderAsync(
        string symbol, 
        string? orderId = null, 
        string? clientOrderId = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// 获取订单详情
    /// </summary>
    Task<OrderInfo?> GetOrderAsync(
        string symbol, 
        string? orderId = null, 
        string? clientOrderId = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// 获取当前挂单
    /// </summary>
    Task<IReadOnlyList<OrderInfo>> GetOpenOrdersAsync(
        string? symbol = null, 
        CancellationToken ct = default);
}

// ============ Request/Response Models ============

/// <summary>
/// 下单请求
/// </summary>
public record OrderRequest
{
    /// <summary>交易对 (e.g., "BTCUSDT_SPBL")</summary>
    public required string Symbol { get; init; }
    
    /// <summary>方向: buy, sell</summary>
    public required string Side { get; init; }
    
    /// <summary>订单类型: limit, market</summary>
    public required string OrderType { get; init; }
    
    /// <summary>执行策略: normal, postOnly, fok, ioc</summary>
    public string Force { get; init; } = "normal";
    
    /// <summary>数量</summary>
    public required string Quantity { get; init; }
    
    /// <summary>价格 (限价单必填)</summary>
    public string? Price { get; init; }
    
    /// <summary>客户端订单ID</summary>
    public string? ClientOrderId { get; init; }
}

/// <summary>
/// 下单结果
/// </summary>
public record OrderResult
{
    public bool Success { get; init; }
    public string? OrderId { get; init; }
    public string? ClientOrderId { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// 取消订单结果
/// </summary>
public record CancelOrderResult
{
    public bool Success { get; init; }
    public string? OrderId { get; init; }
    public string? ClientOrderId { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// 订单信息
/// </summary>
public record OrderInfo
{
    public required string OrderId { get; init; }
    public string? ClientOrderId { get; init; }
    public required string Symbol { get; init; }
    public required string Side { get; init; }
    public required string OrderType { get; init; }
    public required string Status { get; init; }
    public decimal Price { get; init; }
    public decimal Quantity { get; init; }
    public decimal FilledQuantity { get; init; }
    public decimal FilledPrice { get; init; }
    public decimal Fee { get; init; }
    public DateTime CreateTime { get; init; }
    public DateTime? UpdateTime { get; init; }
}

/// <summary>
/// 余额信息
/// </summary>
public record BalanceInfo
{
    public required string Currency { get; init; }
    public decimal Balance { get; init; }
    public decimal Available { get; init; }
    public decimal Frozen { get; init; }
}

/// <summary>
/// 行情数据
/// </summary>
public record TickerResponse
{
    public required string Symbol { get; init; }
    public decimal LastPrice { get; init; }
    public decimal BidPrice { get; init; }
    public decimal AskPrice { get; init; }
    public decimal Volume24h { get; init; }
    public decimal Change24h { get; init; }
    public decimal High24h { get; init; }
    public decimal Low24h { get; init; }
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// K线数据
/// </summary>
public record KlineData
{
    public DateTime OpenTime { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public decimal Volume { get; init; }
    public DateTime CloseTime { get; init; }
}
