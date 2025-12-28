using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Trade.Infrastructure.WeexApi;

// ============================================================================
//  WEEX AI Wars Contract API Client
//  - 对应 AI Wars 合约风格：/capi/v2/...
//  - 默认注入为 IWeexApiClient（Weex:Mode=Contract）
// ============================================================================

internal sealed class WeexContractApiClient : WeexApiClientBase, IWeexApiClient
{
    public WeexContractApiClient(
        HttpClient httpClient,
        IOptions<WeexApiConfig> config,
        ILogger<WeexContractApiClient> logger)
        : base(httpClient, config, logger)
    {
    }

    // =========================
    //  Market Data (public)
    // =========================

    public async Task<TickerResponse> GetTickerAsync(string symbol, CancellationToken ct = default)
    {
        // GET /capi/v2/market/ticker?symbol=...
        var root = await GetMarketAsync<JsonElement>(
            "/capi/v2/market/ticker",
            $"?symbol={Uri.EscapeDataString(symbol)}",
            requiresAuth: false,
            ct);

        var payload = UnwrapDataIfPresent(root);
        var tsMs = NormalizeUnixMs(ReadLong(payload, "ts", "timestamp", "time"));

        return new TickerResponse
        {
            Symbol = symbol,
            LastPrice = ReadDecimal(payload, "last", "lastPrice", "close", "price"),
            BidPrice = ReadDecimal(payload, "bestBid", "bid", "bidPrice", "buy"),
            AskPrice = ReadDecimal(payload, "bestAsk", "ask", "askPrice", "sell"),
            Volume24h = ReadDecimal(payload, "baseVolume", "volume", "vol", "volume24h"),
            Change24h = ReadDecimal(payload, "changeUtc24h", "change24h", "change"),
            High24h = ReadDecimal(payload, "high24h", "high"),
            Low24h = ReadDecimal(payload, "low24h", "low"),
            Timestamp = tsMs > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(tsMs).UtcDateTime
                : DateTime.UtcNow
        };
    }

    public async Task<IReadOnlyList<KlineData>> GetKlinesAsync(
        string symbol,
        string interval,
        int limit = 100,
        CancellationToken ct = default)
    {
        // GET /capi/v2/market/candles?symbol=...&granularity=...&limit=...
        var root = await GetMarketAsync<JsonElement>(
            "/capi/v2/market/candles",
            $"?symbol={Uri.EscapeDataString(symbol)}&granularity={Uri.EscapeDataString(interval)}&limit={limit}",
            requiresAuth: false,
            ct);

        var payload = UnwrapDataIfPresent(root);
        if (payload.ValueKind != JsonValueKind.Array)
            return Array.Empty<KlineData>();

        var span = ParseIntervalToTimeSpanOrZero(interval);
        var list = new List<KlineData>();

        foreach (var item in payload.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array)
                continue;

            var parts = item.EnumerateArray().Select(ToInvariantString).ToList();
            if (parts.Count < 5)
                continue;

            var openMs = NormalizeUnixMs(ParseLong(parts[0]));
            var openTime = DateTimeOffset.FromUnixTimeMilliseconds(openMs).UtcDateTime;

            list.Add(new KlineData
            {
                OpenTime = openTime,
                Open = ParseDecimal(parts.ElementAtOrDefault(1)),
                High = ParseDecimal(parts.ElementAtOrDefault(2)),
                Low = ParseDecimal(parts.ElementAtOrDefault(3)),
                Close = ParseDecimal(parts.ElementAtOrDefault(4)),
                Volume = ParseDecimal(parts.ElementAtOrDefault(5)),
                CloseTime = span > TimeSpan.Zero ? openTime.Add(span) : openTime
            });
        }

        return list;
    }

    // =========================
    //  Account (auth)
    // =========================

    public async Task<IReadOnlyList<BalanceInfo>> GetBalancesAsync(CancellationToken ct = default)
    {
        // GET /capi/v2/account/assets
        var root = await GetTradingAsync<JsonElement>(
            "/capi/v2/account/assets",
            "",
            requiresAuth: true,
            ct);

        var payload = UnwrapDataIfPresent(root);
        if (payload.ValueKind != JsonValueKind.Array)
            return Array.Empty<BalanceInfo>();

        var list = new List<BalanceInfo>();
        foreach (var item in payload.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var currency = ReadString(item, "coinName", "currency", "coin") ?? "";
            if (string.IsNullOrWhiteSpace(currency))
                continue;

            list.Add(new BalanceInfo
            {
                Currency = currency,
                Balance = ReadDecimal(item, "equity", "balance", "total"),
                Available = ReadDecimal(item, "available", "availableBalance", "free"),
                Frozen = ReadDecimal(item, "frozen", "hold", "locked")
            });
        }

        return list;
    }

    public async Task<BalanceInfo?> GetBalanceAsync(string currency, CancellationToken ct = default)
    {
        var balances = await GetBalancesAsync(ct);
        return balances.FirstOrDefault(b => b.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase));
    }

    // =========================
    //  Trading (auth)
    // =========================

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        // --------------------------------------------------------------
        //  AI Wars 合约下单（最小映射：通过预检即可）
        //  POST /capi/v2/order/placeOrder
        //  body: { symbol, client_oid, size, type, order_type, match_price, price }
        // --------------------------------------------------------------
        var clientOid = request.ClientOrderId ?? GenerateClientOrderId();
        var isLimit = string.Equals(request.OrderType, "limit", StringComparison.OrdinalIgnoreCase);
        if (!isLimit)
        {
            return new OrderResult
            {
                Success = false,
                ErrorCode = "UNSUPPORTED",
                ErrorMessage = "AI Wars contract sample currently supports only limit orders. Use orderType=limit with a price."
            };
        }

        if (string.IsNullOrWhiteSpace(request.Price))
        {
            return new OrderResult
            {
                Success = false,
                ErrorCode = "INVALID_REQUEST",
                ErrorMessage = "Limit order requires price."
            };
        }

        // buy  -> 1 (open long) ; sell -> 2 (open short)
        var type = string.Equals(request.Side, "buy", StringComparison.OrdinalIgnoreCase) ? "1" : "2";

        var body = new
        {
            symbol = request.Symbol,
            client_oid = clientOid,
            size = request.Quantity,
            type,
            order_type = "0",
            match_price = "0",
            price = request.Price
        };

        try
        {
            var raw = await PostTradingRawAsync(
                "/capi/v2/order/placeOrder",
                "",
                body,
                requiresAuth: true,
                ct);

            var placed = JsonSerializer.Deserialize<ContractPlaceOrderResponse>(raw, JsonOptions);
            if (!string.IsNullOrWhiteSpace(placed?.OrderId))
            {
                return new OrderResult
                {
                    Success = true,
                    OrderId = placed!.OrderId,
                    ClientOrderId = placed.ClientOid ?? clientOid
                };
            }

            return new OrderResult
            {
                Success = false,
                ErrorCode = "WEEX_CONTRACT_ERROR",
                ErrorMessage = $"Unexpected contract response: {raw}"
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to place AI Wars contract order for {Symbol}", request.Symbol);
            return new OrderResult
            {
                Success = false,
                ErrorCode = "CLIENT_ERROR",
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<CancelOrderResult> CancelOrderAsync(
        string symbol,
        string? orderId = null,
        string? clientOrderId = null,
        CancellationToken ct = default)
    {
        // POST /capi/v2/order/cancel_order
        var body = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(orderId))
            body["orderId"] = orderId;
        if (!string.IsNullOrWhiteSpace(clientOrderId))
            body["clientOid"] = clientOrderId;

        if (body.Count == 0)
        {
            return new CancelOrderResult
            {
                Success = false,
                ErrorCode = "INVALID_REQUEST",
                ErrorMessage = "Either orderId or clientOrderId is required."
            };
        }

        try
        {
            var raw = await PostTradingRawAsync(
                "/capi/v2/order/cancel_order",
                "",
                body,
                requiresAuth: true,
                ct);

            using var doc = JsonDocument.Parse(raw);
            var payload = UnwrapDataIfPresent(doc.RootElement);
            var dto = payload.Deserialize<CancelOrderDto>(JsonOptions);

            if (dto?.Result == true)
            {
                return new CancelOrderResult
                {
                    Success = true,
                    OrderId = dto.OrderId,
                    ClientOrderId = dto.ClientOid
                };
            }

            return new CancelOrderResult
            {
                Success = false,
                ErrorCode = dto?.ErrCode ?? "WEEX_CONTRACT_ERROR",
                ErrorMessage = dto?.ErrMsg ?? $"Unexpected contract response: {raw}"
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to cancel AI Wars contract order {OrderId}", orderId ?? clientOrderId);
            return new CancelOrderResult
            {
                Success = false,
                ErrorCode = "CLIENT_ERROR",
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<OrderInfo?> GetOrderAsync(
        string symbol,
        string? orderId = null,
        string? clientOrderId = null,
        CancellationToken ct = default)
    {
        // GET /capi/v2/order/detail?orderId=...
        if (string.IsNullOrWhiteSpace(orderId))
            throw new WeexApiException("AI Wars contract order detail requires orderId.", errorCode: "INVALID_REQUEST");

        var root = await GetTradingAsync<JsonElement>(
            "/capi/v2/order/detail",
            $"?orderId={Uri.EscapeDataString(orderId)}",
            requiresAuth: true,
            ct);

        var payload = UnwrapDataIfPresent(root);
        if (payload.ValueKind != JsonValueKind.Object)
            return null;

        return MapContractOrder(payload, symbolFallback: symbol);
    }

    public async Task<IReadOnlyList<OrderInfo>> GetOpenOrdersAsync(string? symbol = null, CancellationToken ct = default)
    {
        // GET /capi/v2/order/current
        var query = string.IsNullOrWhiteSpace(symbol) ? "" : $"?symbol={Uri.EscapeDataString(symbol)}";
        var root = await GetTradingAsync<JsonElement>(
            "/capi/v2/order/current",
            query,
            requiresAuth: true,
            ct);

        var payload = UnwrapDataIfPresent(root);
        var orders = ExtractContractOrderArray(payload);

        return orders
            .Select(o => MapContractOrder(o, symbolFallback: symbol ?? ""))
            .Where(o => o != null)
            .Cast<OrderInfo>()
            .ToList();
    }

    // =========================
    //  Contract JSON helpers
    // =========================

    private static JsonElement UnwrapDataIfPresent(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) &&
            data.ValueKind != JsonValueKind.Null && data.ValueKind != JsonValueKind.Undefined)
        {
            return data;
        }

        return root;
    }

    private static IReadOnlyList<JsonElement> ExtractContractOrderArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToList();

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "list", "orderList", "items", "rows", "records" })
            {
                if (TryGetPropertyInsensitive(root, key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    return arr.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToList();
            }

            if (TryGetPropertyInsensitive(root, "order_id", out _) || TryGetPropertyInsensitive(root, "orderId", out _))
                return new[] { root };
        }

        return Array.Empty<JsonElement>();
    }

    private static OrderInfo? MapContractOrder(JsonElement order, string symbolFallback)
    {
        var orderId = ReadString(order, "order_id", "orderId") ?? "";
        if (string.IsNullOrWhiteSpace(orderId))
            return null;

        var symbol = ReadString(order, "symbol") ?? symbolFallback;
        if (string.IsNullOrWhiteSpace(symbol))
            symbol = symbolFallback;

        var type = ReadString(order, "type", "side") ?? "";
        var side = NormalizeContractSide(type);

        var orderType = ReadString(order, "order_type", "orderType") ?? "";
        var status = ReadString(order, "status") ?? "";

        var createMs = NormalizeUnixMs(ReadLong(order, "createTime", "create_time", "ctime", "ts"));
        var updateMs = NormalizeUnixMs(ReadLong(order, "updateTime", "update_time", "mtime"));

        return new OrderInfo
        {
            OrderId = orderId,
            ClientOrderId = ReadString(order, "client_oid", "clientOid", "clientOrderId"),
            Symbol = symbol,
            Side = string.IsNullOrWhiteSpace(side) ? (string.IsNullOrWhiteSpace(type) ? "unknown" : type) : side,
            OrderType = string.IsNullOrWhiteSpace(orderType) ? "unknown" : orderType,
            Status = string.IsNullOrWhiteSpace(status) ? "unknown" : status,
            Price = ReadDecimal(order, "price"),
            Quantity = ReadDecimal(order, "size", "qty", "quantity"),
            FilledQuantity = ReadDecimal(order, "filled_qty", "filledQty", "filledSize"),
            FilledPrice = ReadDecimal(order, "price_avg", "priceAvg", "filledPrice", "avgPrice"),
            Fee = ReadDecimal(order, "fee"),
            CreateTime = createMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(createMs).UtcDateTime : DateTime.UtcNow,
            UpdateTime = updateMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(updateMs).UtcDateTime : null
        };
    }

    private static string NormalizeContractSide(string typeOrSide)
    {
        if (string.IsNullOrWhiteSpace(typeOrSide))
            return "";

        var v = typeOrSide.Trim();
        if (string.Equals(v, "buy", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(v, "sell", StringComparison.OrdinalIgnoreCase))
            return v.ToLowerInvariant();

        if (v.Contains("long", StringComparison.OrdinalIgnoreCase))
            return "buy";
        if (v.Contains("short", StringComparison.OrdinalIgnoreCase))
            return "sell";

        return "";
    }

    private static bool TryGetPropertyInsensitive(JsonElement obj, string name, out JsonElement value)
    {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object)
            return false;

        if (obj.TryGetProperty(name, out value))
            return true;

        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        return false;
    }

    private static string? ReadString(JsonElement obj, params string[] names)
    {
        foreach (var n in names)
        {
            if (!TryGetPropertyInsensitive(obj, n, out var el))
                continue;

            if (el.ValueKind == JsonValueKind.String)
                return el.GetString();

            if (el.ValueKind == JsonValueKind.Number)
                return el.GetRawText();
        }

        return null;
    }

    private static long ReadLong(JsonElement obj, params string[] names)
    {
        foreach (var n in names)
        {
            if (!TryGetPropertyInsensitive(obj, n, out var el))
                continue;

            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var l))
                return l;

            if (el.ValueKind == JsonValueKind.String && long.TryParse(el.GetString(), out var ls))
                return ls;
        }

        return 0;
    }

    private static decimal ReadDecimal(JsonElement obj, params string[] names)
    {
        foreach (var n in names)
        {
            if (!TryGetPropertyInsensitive(obj, n, out var el))
                continue;

            if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d))
                return d;

            if (el.ValueKind == JsonValueKind.String && decimal.TryParse(el.GetString(), out var ds))
                return ds;
        }

        return 0m;
    }

    private static string ToInvariantString(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString() ?? "",
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => el.GetRawText()
        };
    }

    // =========================
    //  DTOs (Contract)
    // =========================

    private record ContractPlaceOrderResponse
    {
        [JsonPropertyName("order_id")]
        public string? OrderId { get; init; }

        [JsonPropertyName("client_oid")]
        public string? ClientOid { get; init; }
    }

    private record CancelOrderDto
    {
        [JsonPropertyName("order_id")]
        public string? OrderId { get; init; }

        [JsonPropertyName("client_oid")]
        public string? ClientOid { get; init; }

        public bool Result { get; init; }

        [JsonPropertyName("err_code")]
        public string? ErrCode { get; init; }

        [JsonPropertyName("err_msg")]
        public string? ErrMsg { get; init; }
    }
}


