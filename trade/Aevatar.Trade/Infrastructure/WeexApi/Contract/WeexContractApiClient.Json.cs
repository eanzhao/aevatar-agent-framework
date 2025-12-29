using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aevatar.Trade.Infrastructure.WeexApi;

// ============================================================================
//  WEEX Contract Client - JSON helpers / DTOs
// ============================================================================

internal sealed partial class WeexContractApiClient
{
    private static JsonElement UnwrapDataIfPresent(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) &&
            data.ValueKind != JsonValueKind.Null && data.ValueKind != JsonValueKind.Undefined)
        {
            return data;
        }

        return root;
    }

    private static IReadOnlyList<JsonElement> ExtractObjectArrayDeep(JsonElement root, int depth = 0)
    {
        if (depth > 5)
            return Array.Empty<JsonElement>();

        if (root.ValueKind == JsonValueKind.Array)
            return root.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToList();

        if (root.ValueKind != JsonValueKind.Object)
            return Array.Empty<JsonElement>();

        // Common wrappers: data / list / rows / records / result...
        foreach (var key in new[]
                 {
                     "data",
                     "list",
                     "items",
                     "rows",
                     "records",
                     "result",
                     "positionList",
                     "positions",
                     "fillList",
                     "fills",
                     "orderList"
                 })
        {
            if (!TryGetPropertyInsensitive(root, key, out var inner) ||
                inner.ValueKind == JsonValueKind.Null ||
                inner.ValueKind == JsonValueKind.Undefined)
            {
                continue;
            }

            var got = ExtractObjectArrayDeep(inner, depth + 1);
            if (got.Count > 0)
                return got;
        }

        return Array.Empty<JsonElement>();
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

    private static long? ReadLongNullable(JsonElement obj, params string[] names)
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

        return null;
    }

    private static decimal ReadDecimal(JsonElement obj, params string[] names)
    {
        foreach (var n in names)
        {
            if (!TryGetPropertyInsensitive(obj, n, out var el))
                continue;

            if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d))
                return d;

            if (el.ValueKind == JsonValueKind.String &&
                decimal.TryParse(el.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var ds))
                return ds;
        }

        return 0m;
    }

    private static decimal? ReadDecimalNullable(JsonElement obj, params string[] names)
    {
        foreach (var n in names)
        {
            if (!TryGetPropertyInsensitive(obj, n, out var el))
                continue;

            if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d))
                return d;

            if (el.ValueKind == JsonValueKind.String &&
                decimal.TryParse(el.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var ds))
                return ds;
        }

        return null;
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


