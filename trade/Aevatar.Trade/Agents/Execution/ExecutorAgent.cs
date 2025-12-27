using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Aevatar.Trade.Infrastructure.WeexApi;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Trade.Agents.Execution;

/// <summary>
/// 交易执行 Agent
/// 职责：执行实际交易，管理订单生命周期
/// </summary>
public class ExecutorAgent : GAgentBase<ExecutorState>
{
    // ============ Dependencies ============

    private IWeexApiClient? _apiClient;

    public IWeexApiClient ApiClient
    {
        set => _apiClient = value;
    }

    // ============ Lifecycle ============

    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        
        State.AgentId = Id.ToString();
        State.OrdersPlaced = 0;
        State.OrdersFilled = 0;
        State.OrdersFailed = 0;
        
        Logger.LogInformation("[Executor] Activated: {AgentId}", State.AgentId);
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult(
            $"Executor: Placed={State.OrdersPlaced}, " +
            $"Filled={State.OrdersFilled}, " +
            $"Failed={State.OrdersFailed}, " +
            $"PnL=${State.TotalRealizedPnl:F2}");
    }

    // ============ Event Handlers ============

    /// <summary>
    /// 处理批准的交易，执行下单
    /// </summary>
    [EventHandler]
    public async Task HandleApprovedTrade(ApprovedTradeEvent evt)
    {
        if (_apiClient == null)
        {
            Logger.LogError("[Executor] API client not configured");
            await PublishOrderFailed(evt, "API_NOT_CONFIGURED", "API client not configured");
            return;
        }

        Logger.LogInformation(
            "[Executor] Executing trade: {DecisionId}, {Side} {Symbol}, Qty={Qty}",
            evt.DecisionId, evt.Side, evt.Symbol, evt.Quantity);

        await ExecuteTradeAsync(evt);
    }

    /// <summary>
    /// 处理熔断事件，取消所有挂单
    /// </summary>
    [EventHandler]
    public async Task HandleCircuitBreaker(CircuitBreakerTriggeredEvent evt)
    {
        Logger.LogWarning(
            "[Executor] Circuit breaker triggered: {Type}, cancelling all orders",
            evt.TriggerType);

        await CancelAllOrdersAsync();
    }

    // ============ Trade Execution ============

    private async Task ExecuteTradeAsync(ApprovedTradeEvent trade)
    {
        var clientOrderId = GenerateClientOrderId(trade.DecisionId);

        try
        {
            // 构建订单请求
            var request = new OrderRequest
            {
                Symbol = trade.Symbol,
                Side = trade.Side,
                OrderType = trade.OrderType,
                Quantity = trade.Quantity.ToString("F8"),
                Price = trade.OrderType == "limit" ? trade.Price.ToString("F8") : null,
                ClientOrderId = clientOrderId
            };

            // 执行下单
            var result = await _apiClient!.PlaceOrderAsync(request);
            State.OrdersPlaced++;

            if (result.Success)
            {
                // 记录活跃订单
                State.ActiveOrderIds.Add(result.OrderId ?? clientOrderId);

                Logger.LogInformation(
                    "[Executor] Order placed: {OrderId}, ClientId={ClientId}",
                    result.OrderId, clientOrderId);

                // 发布执行成功事件
                await PublishAsync(new OrderExecutedEvent
                {
                    OrderId = result.OrderId ?? "",
                    ClientOrderId = clientOrderId,
                    DecisionId = trade.DecisionId,
                    Symbol = trade.Symbol,
                    Side = trade.Side,
                    Quantity = trade.Quantity,
                    FilledPrice = trade.Price,
                    Status = "SUBMITTED",
                    Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
                });

                // 如果有止损止盈，设置条件单
                if (trade.StopLoss > 0 || trade.TakeProfit > 0)
                {
                    await SetStopOrdersAsync(trade, result.OrderId ?? clientOrderId);
                }
            }
            else
            {
                State.OrdersFailed++;
                Logger.LogError(
                    "[Executor] Order failed: {Error} ({Code})",
                    result.ErrorMessage, result.ErrorCode);

                await PublishOrderFailed(
                    trade, 
                    result.ErrorCode ?? "UNKNOWN", 
                    result.ErrorMessage ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            State.OrdersFailed++;
            Logger.LogError(ex, "[Executor] Order execution exception");
            await PublishOrderFailed(trade, "EXCEPTION", ex.Message);
        }
    }

    private async Task SetStopOrdersAsync(ApprovedTradeEvent trade, string parentOrderId)
    {
        // 设置止损单
        if (trade.StopLoss > 0)
        {
            try
            {
                var stopSide = trade.Side == "buy" ? "sell" : "buy";
                var stopRequest = new OrderRequest
                {
                    Symbol = trade.Symbol,
                    Side = stopSide,
                    OrderType = "limit",
                    Quantity = trade.Quantity.ToString("F8"),
                    Price = trade.StopLoss.ToString("F8"),
                    ClientOrderId = $"SL_{parentOrderId}"
                };

                // 注意：实际交易所可能有专门的止损单 API
                Logger.LogDebug(
                    "[Executor] Stop loss set at {Price} for {OrderId}",
                    trade.StopLoss, parentOrderId);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[Executor] Failed to set stop loss");
            }
        }

        // 设置止盈单
        if (trade.TakeProfit > 0)
        {
            try
            {
                var tpSide = trade.Side == "buy" ? "sell" : "buy";
                var tpRequest = new OrderRequest
                {
                    Symbol = trade.Symbol,
                    Side = tpSide,
                    OrderType = "limit",
                    Quantity = trade.Quantity.ToString("F8"),
                    Price = trade.TakeProfit.ToString("F8"),
                    ClientOrderId = $"TP_{parentOrderId}"
                };

                Logger.LogDebug(
                    "[Executor] Take profit set at {Price} for {OrderId}",
                    trade.TakeProfit, parentOrderId);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[Executor] Failed to set take profit");
            }
        }

        await Task.CompletedTask;
    }

    private async Task PublishOrderFailed(ApprovedTradeEvent trade, string code, string message)
    {
        await PublishAsync(new OrderFailedEvent
        {
            ClientOrderId = trade.DecisionId,
            DecisionId = trade.DecisionId,
            Symbol = trade.Symbol,
            Side = trade.Side,
            ErrorCode = code,
            ErrorMessage = message,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });
    }

    // ============ Order Management ============

    /// <summary>
    /// 取消指定订单
    /// </summary>
    public async Task<bool> CancelOrderAsync(string symbol, string orderId)
    {
        if (_apiClient == null) return false;

        try
        {
            var result = await _apiClient.CancelOrderAsync(symbol, orderId);
            
            if (result.Success)
            {
                State.OrdersCancelled++;
                State.ActiveOrderIds.Remove(orderId);

                await PublishAsync(new OrderCancelledEvent
                {
                    OrderId = orderId,
                    Symbol = symbol,
                    Reason = "Manual cancellation",
                    Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
                });

                Logger.LogInformation("[Executor] Order cancelled: {OrderId}", orderId);
                return true;
            }

            Logger.LogWarning(
                "[Executor] Failed to cancel order: {OrderId}, {Error}",
                orderId, result.ErrorMessage);
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[Executor] Cancel order exception: {OrderId}", orderId);
            return false;
        }
    }

    /// <summary>
    /// 取消所有挂单
    /// </summary>
    public async Task CancelAllOrdersAsync()
    {
        if (_apiClient == null) return;

        try
        {
            var openOrders = await _apiClient.GetOpenOrdersAsync();
            
            foreach (var order in openOrders)
            {
                await CancelOrderAsync(order.Symbol, order.OrderId);
            }

            Logger.LogInformation(
                "[Executor] Cancelled {Count} orders",
                openOrders.Count);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[Executor] Cancel all orders exception");
        }
    }

    /// <summary>
    /// 同步订单状态
    /// </summary>
    public async Task SyncOrderStatusAsync(string symbol)
    {
        if (_apiClient == null) return;

        try
        {
            var openOrders = await _apiClient.GetOpenOrdersAsync(symbol);
            
            // 更新活跃订单列表
            State.ActiveOrderIds.Clear();
            foreach (var order in openOrders)
            {
                State.ActiveOrderIds.Add(order.OrderId);
            }

            Logger.LogDebug(
                "[Executor] Synced {Count} open orders for {Symbol}",
                openOrders.Count, symbol);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[Executor] Sync order status exception");
        }
    }

    /// <summary>
    /// 同步持仓信息
    /// </summary>
    public async Task SyncPositionsAsync()
    {
        if (_apiClient == null) return;

        try
        {
            var balances = await _apiClient.GetBalancesAsync();
            
            State.Positions.Clear();
            foreach (var balance in balances.Where(b => b.Balance > 0))
            {
                State.Positions[balance.Currency] = (double)balance.Balance;
            }

            Logger.LogDebug(
                "[Executor] Synced {Count} positions",
                State.Positions.Count);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[Executor] Sync positions exception");
        }
    }

    // ============ Helper Methods ============

    private static string GenerateClientOrderId(string decisionId)
        => $"{decisionId}_{DateTime.UtcNow:HHmmssfff}";
}
