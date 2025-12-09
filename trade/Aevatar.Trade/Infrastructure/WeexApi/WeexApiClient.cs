using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Trade.Infrastructure.WeexApi;

/// <summary>
/// WEEX API 客户端实现
/// 官方文档: https://www.weex.com/api-doc/spot/introduction/APIBriefIntroduction
/// </summary>
public class WeexApiClient : IWeexApiClient
{
    private readonly HttpClient _httpClient;
    private readonly WeexApiConfig _config;
    private readonly ILogger<WeexApiClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public WeexApiClient(
        HttpClient httpClient,
        IOptions<WeexApiConfig> config,
        ILogger<WeexApiClient> logger)
    {
        _httpClient = httpClient;
        _config = config.Value;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    // ============ Market Data ============

    public async Task<TickerResponse> GetTickerAsync(string symbol, CancellationToken ct = default)
    {
        var response = await GetAsync<WeexResponse<TickerDto>>(
            $"/api/v2/market/ticker?symbol={symbol}", 
            requiresAuth: false, 
            ct);

        var data = response.Data ?? throw new WeexApiException("Empty ticker response");
        
        return new TickerResponse
        {
            Symbol = symbol,
            LastPrice = ParseDecimal(data.Last),
            BidPrice = ParseDecimal(data.BestBid),
            AskPrice = ParseDecimal(data.BestAsk),
            Volume24h = ParseDecimal(data.BaseVolume),
            Change24h = ParseDecimal(data.ChangeUtc24h),
            High24h = ParseDecimal(data.High24h),
            Low24h = ParseDecimal(data.Low24h),
            Timestamp = DateTime.UtcNow
        };
    }

    public async Task<IReadOnlyList<KlineData>> GetKlinesAsync(
        string symbol, 
        string interval, 
        int limit = 100, 
        CancellationToken ct = default)
    {
        var response = await GetAsync<WeexResponse<List<List<string>>>>(
            $"/api/v2/market/klines?symbol={symbol}&period={interval}&limit={limit}",
            requiresAuth: false,
            ct);

        var data = response.Data ?? new List<List<string>>();
        
        return data.Select(k => new KlineData
        {
            OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(k[0])).UtcDateTime,
            Open = ParseDecimal(k[1]),
            High = ParseDecimal(k[2]),
            Low = ParseDecimal(k[3]),
            Close = ParseDecimal(k[4]),
            Volume = ParseDecimal(k[5]),
            CloseTime = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(k[6])).UtcDateTime
        }).ToList();
    }

    // ============ Account ============

    public async Task<IReadOnlyList<BalanceInfo>> GetBalancesAsync(CancellationToken ct = default)
    {
        var response = await GetAsync<WeexResponse<List<BalanceDto>>>(
            "/api/v2/account/balance",
            requiresAuth: true,
            ct);

        var data = response.Data ?? new List<BalanceDto>();
        
        return data.Select(b => new BalanceInfo
        {
            Currency = b.Currency,
            Balance = ParseDecimal(b.Balance),
            Available = ParseDecimal(b.Available),
            Frozen = ParseDecimal(b.Frozen)
        }).ToList();
    }

    public async Task<BalanceInfo?> GetBalanceAsync(string currency, CancellationToken ct = default)
    {
        var balances = await GetBalancesAsync(ct);
        return balances.FirstOrDefault(b => 
            b.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase));
    }

    // ============ Trading ============

    public async Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        var body = new
        {
            symbol = request.Symbol,
            side = request.Side,
            orderType = request.OrderType,
            force = request.Force,
            quantity = request.Quantity,
            price = request.Price ?? "0",
            clientOrderId = request.ClientOrderId ?? GenerateClientOrderId()
        };

        try
        {
            var response = await PostAsync<WeexResponse<OrderResultDto>>(
                "/api/v2/trade/orders",
                body,
                ct);

            if (response.Code == "00000" && response.Data != null)
            {
                return new OrderResult
                {
                    Success = true,
                    OrderId = response.Data.OrderId,
                    ClientOrderId = response.Data.ClientOrderId
                };
            }

            return new OrderResult
            {
                Success = false,
                ErrorCode = response.Code,
                ErrorMessage = response.Msg
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to place order for {Symbol}", request.Symbol);
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
        var body = new Dictionary<string, string> { ["symbol"] = symbol };
        
        if (!string.IsNullOrEmpty(orderId))
            body["orderId"] = orderId;
        if (!string.IsNullOrEmpty(clientOrderId))
            body["clientOid"] = clientOrderId;

        try
        {
            var response = await PostAsync<WeexResponse<CancelOrderDto>>(
                "/api/v2/trade/cancel-order",
                body,
                ct);

            if (response.Code == "00000" && response.Data?.Result == true)
            {
                return new CancelOrderResult
                {
                    Success = true,
                    OrderId = response.Data.OrderId,
                    ClientOrderId = response.Data.ClientOid
                };
            }

            return new CancelOrderResult
            {
                Success = false,
                ErrorCode = response.Data?.ErrCode ?? response.Code,
                ErrorMessage = response.Data?.ErrMsg ?? response.Msg
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel order {OrderId}", orderId ?? clientOrderId);
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
        var query = $"symbol={symbol}";
        if (!string.IsNullOrEmpty(orderId))
            query += $"&orderId={orderId}";
        if (!string.IsNullOrEmpty(clientOrderId))
            query += $"&clientOid={clientOrderId}";

        var response = await GetAsync<WeexResponse<OrderDto>>(
            $"/api/v2/trade/order?{query}",
            requiresAuth: true,
            ct);

        if (response.Data == null) return null;

        return MapOrderDto(response.Data);
    }

    public async Task<IReadOnlyList<OrderInfo>> GetOpenOrdersAsync(
        string? symbol = null, 
        CancellationToken ct = default)
    {
        var query = string.IsNullOrEmpty(symbol) ? "" : $"?symbol={symbol}";
        
        var response = await GetAsync<WeexResponse<List<OrderDto>>>(
            $"/api/v2/trade/open-orders{query}",
            requiresAuth: true,
            ct);

        var data = response.Data ?? new List<OrderDto>();
        return data.Select(MapOrderDto).ToList();
    }

    // ============ Private Methods ============

    private async Task<T> GetAsync<T>(string path, bool requiresAuth, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        
        if (requiresAuth)
            AddAuthHeaders(request, HttpMethod.Get, path, null);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, ct)
            ?? throw new WeexApiException($"Failed to deserialize response from {path}");
    }

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken ct)
    {
        var jsonBody = JsonSerializer.Serialize(body, _jsonOptions);
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };

        AddAuthHeaders(request, HttpMethod.Post, path, jsonBody);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, ct)
            ?? throw new WeexApiException($"Failed to deserialize response from {path}");
    }

    private void AddAuthHeaders(HttpRequestMessage request, HttpMethod method, string path, string? body)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var signature = GenerateSignature(timestamp, method.Method, path, body);

        request.Headers.Add("ACCESS-KEY", _config.ApiKey);
        request.Headers.Add("ACCESS-SIGN", signature);
        request.Headers.Add("ACCESS-PASSPHRASE", _config.Passphrase);
        request.Headers.Add("ACCESS-TIMESTAMP", timestamp);
        request.Headers.Add("locale", "en-US");
    }

    private string GenerateSignature(string timestamp, string method, string path, string? body)
    {
        var message = timestamp + method.ToUpper() + path + (body ?? "");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_config.ApiSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        return Convert.ToBase64String(hash);
    }

    private static string GenerateClientOrderId()
        => $"{DateTime.UtcNow:yyyyMMddHHmmssfff}{Random.Shared.Next(100000, 999999)}";

    private static decimal ParseDecimal(string? value)
        => decimal.TryParse(value, out var result) ? result : 0m;

    private static OrderInfo MapOrderDto(OrderDto dto) => new()
    {
        OrderId = dto.OrderId,
        ClientOrderId = dto.ClientOid,
        Symbol = dto.Symbol,
        Side = dto.Side,
        OrderType = dto.OrderType,
        Status = dto.Status,
        Price = ParseDecimal(dto.Price),
        Quantity = ParseDecimal(dto.Size),
        FilledQuantity = ParseDecimal(dto.FilledSize),
        FilledPrice = ParseDecimal(dto.FilledPrice),
        Fee = ParseDecimal(dto.Fee),
        CreateTime = DateTimeOffset.FromUnixTimeMilliseconds(dto.CreateTime).UtcDateTime,
        UpdateTime = dto.UpdateTime > 0 
            ? DateTimeOffset.FromUnixTimeMilliseconds(dto.UpdateTime).UtcDateTime 
            : null
    };

    // ============ DTOs ============

    private record WeexResponse<T>
    {
        public string Code { get; init; } = "";
        public string Msg { get; init; } = "";
        public long RequestTime { get; init; }
        public T? Data { get; init; }
    }

    private record TickerDto
    {
        public string? Last { get; init; }
        public string? BestBid { get; init; }
        public string? BestAsk { get; init; }
        public string? BaseVolume { get; init; }
        public string? ChangeUtc24h { get; init; }
        public string? High24h { get; init; }
        public string? Low24h { get; init; }
    }

    private record BalanceDto
    {
        public string Currency { get; init; } = "";
        public string? Balance { get; init; }
        public string? Available { get; init; }
        public string? Frozen { get; init; }
    }

    private record OrderResultDto
    {
        public string? OrderId { get; init; }
        public string? ClientOrderId { get; init; }
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

    private record OrderDto
    {
        public string OrderId { get; init; } = "";
        public string? ClientOid { get; init; }
        public string Symbol { get; init; } = "";
        public string Side { get; init; } = "";
        public string OrderType { get; init; } = "";
        public string Status { get; init; } = "";
        public string? Price { get; init; }
        public string? Size { get; init; }
        public string? FilledSize { get; init; }
        public string? FilledPrice { get; init; }
        public string? Fee { get; init; }
        public long CreateTime { get; init; }
        public long UpdateTime { get; init; }
    }
}

/// <summary>
/// WEEX API 配置
/// </summary>
public class WeexApiConfig
{
    public string BaseUrl { get; set; } = "https://api-spot.weex.com";
    public string ApiKey { get; set; } = "";
    public string ApiSecret { get; set; } = "";
    public string Passphrase { get; set; } = "";
}

/// <summary>
/// WEEX API 异常
/// </summary>
public class WeexApiException : Exception
{
    public string? ErrorCode { get; }

    public WeexApiException(string message, string? errorCode = null) : base(message)
    {
        ErrorCode = errorCode;
    }
}
