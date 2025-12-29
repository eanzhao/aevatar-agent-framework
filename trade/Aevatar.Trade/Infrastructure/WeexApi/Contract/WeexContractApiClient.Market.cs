using System.Text.Json;

namespace Aevatar.Trade.Infrastructure.WeexApi;

// ============================================================================
//  WEEX Contract Client - Market (行情)
//  - /capi/v2/market/*
//  - AI Wars 网关经常会对“无签名/无 UA”请求返回 HTML 403，所以这里统一 requiresAuth=true
// ============================================================================

internal sealed partial class WeexContractApiClient
{
    public async Task<TickerResponse> GetTickerAsync(string symbol, CancellationToken ct = default)
    {
        // GET /capi/v2/market/ticker?symbol=...
        var root = await GetMarketAsync<JsonElement>(
            "/capi/v2/market/ticker",
            $"?symbol={Uri.EscapeDataString(symbol)}",
            // AI Wars 环境下，很多节点会对“无签名/无 key 的请求”直接 403（甚至返回 HTML 网关页）。
            // 这里统一走签名请求，避免被网关当作未授权爬虫拦截。
            requiresAuth: true,
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
            requiresAuth: true,
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
}


