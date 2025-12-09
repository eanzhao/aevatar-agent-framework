using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Core;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Trade.Agents.RiskControl;

/// <summary>
/// 风控经理 Agent
/// 职责：评估交易风险，设定止损止盈，必要时否决交易
/// </summary>
public class RiskManagerAgent : AIGAgentBase
{
    // ============ AI Configuration ============

    public override string SystemPrompt { get; set; } = """
        你是风控经理，负责保护资金安全，是交易决策的最后一道防线。

        【风控规则 - 硬性约束】

        1. 单笔风险控制
           - 单笔最大亏损: 不超过总资金的 2%
           - 单笔最大仓位: 不超过总资金的 10%

        2. 总体风险控制
           - 总仓位上限: 不超过总资金的 30%
           - 日最大亏损: 不超过总资金的 5%
           - 连续亏损熔断: 连续 3 次亏损后暂停 1 小时

        3. 止损止盈设置
           - 止损: 1-3% (根据 ATR 动态调整)
           - 止盈: 止损的 1.5-3 倍 (风险回报比)

        【风险评估维度】

        1. 市场风险
           - 当前波动率 (ATR)
           - 流动性状况
           - 极端行情风险

        2. 仓位风险
           - 当前持仓比例
           - 未实现盈亏
           - 风险敞口集中度

        3. 交易风险
           - 入场时机
           - 滑点风险
           - 执行风险

        【输出格式】
        请严格按以下 JSON 格式输出：
        {
            "approved": <true|false>,
            "risk_level": "<LOW|MEDIUM|HIGH|EXTREME>",
            "adjusted_position_pct": <调整后仓位百分比>,
            "stop_loss_pct": <止损百分比>,
            "take_profit_pct": <止盈百分比>,
            "violated_rules": ["违反的规则列表"],
            "risk_notes": "<风险评估备注>",
            "summary": "<一句话风控结论>"
        }

        【否决条件 - 任一触发即否决】
        - 超过日亏损限额
        - 连续亏损未冷却
        - 仓位超限
        - 极端市场条件
        - 置信度过低
        """;

    // ============ State ============

    private readonly RiskManagerState _riskState = new();

    // 风控配置
    private double _maxPositionPct = 10.0;      // 单笔最大仓位 10%
    private double _maxTotalPositionPct = 30.0; // 总仓位上限 30%
    private double _maxLossPerTrade = 2.0;      // 单笔最大亏损 2%
    private double _maxDailyLossPct = 5.0;      // 日最大亏损 5%
    private int _maxConsecutiveLosses = 3;      // 连续亏损熔断
    private int _cooldownMinutes = 60;          // 熔断冷却时间

    // ============ Lifecycle ============

    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        
        _riskState.AgentId = Id.ToString();
        _riskState.IsTradingAllowed = true;
        _riskState.CircuitBreakerStatus = "NORMAL";
        
        Logger.LogInformation("[RiskManager] Activated: {AgentId}", _riskState.AgentId);
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult(
            $"RiskManager: Position={_riskState.PositionRatio:F1}%, " +
            $"DailyPnL={_riskState.DailyPnlPct:F2}%, " +
            $"Approved={_riskState.TradesApproved}, " +
            $"Rejected={_riskState.TradesRejected}");
    }

    /// <summary>
    /// 配置风控参数
    /// </summary>
    public void Configure(
        double maxPositionPct = 10.0,
        double maxTotalPositionPct = 30.0,
        double maxLossPerTrade = 2.0,
        double maxDailyLossPct = 5.0,
        int maxConsecutiveLosses = 3,
        int cooldownMinutes = 60)
    {
        _maxPositionPct = maxPositionPct;
        _maxTotalPositionPct = maxTotalPositionPct;
        _maxLossPerTrade = maxLossPerTrade;
        _maxDailyLossPct = maxDailyLossPct;
        _maxConsecutiveLosses = maxConsecutiveLosses;
        _cooldownMinutes = cooldownMinutes;

        Logger.LogInformation(
            "[RiskManager] Configured: MaxPos={MaxPos}%, MaxTotal={MaxTotal}%, " +
            "MaxLoss={MaxLoss}%, MaxDaily={MaxDaily}%",
            maxPositionPct, maxTotalPositionPct, maxLossPerTrade, maxDailyLossPct);
    }

    /// <summary>
    /// 更新账户信息
    /// </summary>
    public void UpdateAccountInfo(
        double totalEquity,
        double availableBalance,
        double currentPositionValue,
        double unrealizedPnl)
    {
        _riskState.TotalEquity = totalEquity;
        _riskState.AvailableBalance = availableBalance;
        _riskState.CurrentPositionValue = currentPositionValue;
        _riskState.UnrealizedPnl = unrealizedPnl;
        _riskState.PositionRatio = totalEquity > 0 
            ? (currentPositionValue / totalEquity) * 100 
            : 0;
    }

    // ============ Event Handlers ============

    /// <summary>
    /// 处理交易决策，进行风险评估
    /// </summary>
    [EventHandler]
    public async Task HandleTradingDecision(TradingDecisionEvent evt)
    {
        Logger.LogInformation(
            "[RiskManager] Evaluating decision: {DecisionId}, {Direction} {Symbol}",
            evt.DecisionId, evt.Direction, evt.Symbol);

        // 先做硬性规则检查
        var hardCheckResult = PerformHardRuleCheck(evt);
        if (!hardCheckResult.Passed)
        {
            await RejectTrade(evt, hardCheckResult.ViolatedRules, "EXTREME");
            return;
        }

        // AI 辅助风险评估
        await EvaluateWithAIAsync(evt);
    }

    /// <summary>
    /// 处理订单执行结果
    /// </summary>
    [EventHandler]
    public Task HandleOrderExecuted(OrderExecutedEvent evt)
    {
        // 更新盈亏统计
        // 这里简化处理，实际应该跟踪每笔交易的盈亏
        Logger.LogDebug("[RiskManager] Order executed: {OrderId}", evt.OrderId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 处理订单失败
    /// </summary>
    [EventHandler]
    public Task HandleOrderFailed(OrderFailedEvent evt)
    {
        Logger.LogWarning(
            "[RiskManager] Order failed: {ClientOrderId}, Error: {Error}",
            evt.ClientOrderId, evt.ErrorMessage);
        return Task.CompletedTask;
    }

    // ============ Risk Evaluation ============

    private (bool Passed, List<string> ViolatedRules) PerformHardRuleCheck(TradingDecisionEvent evt)
    {
        var violations = new List<string>();

        // 1. 检查熔断状态
        if (!_riskState.IsTradingAllowed)
        {
            violations.Add($"交易已熔断，状态: {_riskState.CircuitBreakerStatus}");
        }

        // 2. 检查日亏损限额
        if (_riskState.DailyPnlPct <= -_maxDailyLossPct)
        {
            violations.Add($"日亏损已达上限: {_riskState.DailyPnlPct:F2}% >= {_maxDailyLossPct}%");
        }

        // 3. 检查连续亏损
        if (_riskState.ConsecutiveLosses >= _maxConsecutiveLosses)
        {
            violations.Add($"连续亏损次数: {_riskState.ConsecutiveLosses} >= {_maxConsecutiveLosses}");
        }

        // 4. 检查总仓位限制
        var newPositionRatio = _riskState.PositionRatio + evt.SuggestedPositionPct;
        if (newPositionRatio > _maxTotalPositionPct)
        {
            violations.Add($"总仓位将超限: {newPositionRatio:F1}% > {_maxTotalPositionPct}%");
        }

        // 5. 检查单笔仓位限制
        if (evt.SuggestedPositionPct > _maxPositionPct)
        {
            violations.Add($"单笔仓位超限: {evt.SuggestedPositionPct:F1}% > {_maxPositionPct}%");
        }

        return (violations.Count == 0, violations);
    }

    private async Task EvaluateWithAIAsync(TradingDecisionEvent evt)
    {
        var prompt = BuildEvaluationPrompt(evt);

        try
        {
            var response = await CompleteAsync(prompt);
            var evaluation = ParseEvaluationResponse(response);

            if (evaluation.Approved)
            {
                await ApproveTrade(evt, evaluation);
            }
            else
            {
                await RejectTrade(evt, evaluation.ViolatedRules, evaluation.RiskLevel);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[RiskManager] AI evaluation failed, rejecting trade");
            await RejectTrade(evt, new List<string> { "风控AI评估失败" }, "HIGH");
        }
    }

    private string BuildEvaluationPrompt(TradingDecisionEvent evt)
    {
        return $"""
            请评估以下交易决策的风险：

            【交易决策】
            - 决策ID: {evt.DecisionId}
            - 交易对: {evt.Symbol}
            - 方向: {evt.Direction}
            - 建议仓位: {evt.SuggestedPositionPct}%
            - 决策置信度: {evt.Confidence}%
            - 决策理由: {evt.Reasoning}

            【当前账户状态】
            - 总权益: ${_riskState.TotalEquity:F2}
            - 可用余额: ${_riskState.AvailableBalance:F2}
            - 当前仓位: {_riskState.PositionRatio:F1}%
            - 今日盈亏: {_riskState.DailyPnlPct:F2}%
            - 未实现盈亏: ${_riskState.UnrealizedPnl:F2}
            - 连续亏损次数: {_riskState.ConsecutiveLosses}
            - 连续盈利次数: {_riskState.ConsecutiveWins}

            【风控配置】
            - 单笔最大仓位: {_maxPositionPct}%
            - 总仓位上限: {_maxTotalPositionPct}%
            - 单笔最大亏损: {_maxLossPerTrade}%
            - 日最大亏损: {_maxDailyLossPct}%

            【分析摘要】
            - 情绪面: {evt.SentimentSummary}
            - 技术面: {evt.TechnicalSummary}
            - 新闻面: {evt.NewsSummary}
            """;
    }

    private RiskEvaluation ParseEvaluationResponse(string response)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(response);
            var root = doc.RootElement;

            var violations = new List<string>();
            if (root.TryGetProperty("violated_rules", out var rulesArray))
            {
                foreach (var rule in rulesArray.EnumerateArray())
                {
                    violations.Add(rule.GetString() ?? "");
                }
            }

            return new RiskEvaluation
            {
                Approved = root.TryGetProperty("approved", out var approved) && approved.GetBoolean(),
                RiskLevel = root.TryGetProperty("risk_level", out var level) 
                    ? level.GetString() ?? "MEDIUM" : "MEDIUM",
                AdjustedPositionPct = root.TryGetProperty("adjusted_position_pct", out var pos) 
                    ? pos.GetDouble() : 10,
                StopLossPct = root.TryGetProperty("stop_loss_pct", out var sl) 
                    ? sl.GetDouble() : 2,
                TakeProfitPct = root.TryGetProperty("take_profit_pct", out var tp) 
                    ? tp.GetDouble() : 4,
                ViolatedRules = violations,
                RiskNotes = root.TryGetProperty("risk_notes", out var notes) 
                    ? notes.GetString() ?? "" : "",
                Summary = root.TryGetProperty("summary", out var summary) 
                    ? summary.GetString() ?? "" : ""
            };
        }
        catch
        {
            return new RiskEvaluation
            {
                Approved = false,
                RiskLevel = "HIGH",
                ViolatedRules = new List<string> { "风控响应解析失败" },
                RiskNotes = response
            };
        }
    }

    private async Task ApproveTrade(TradingDecisionEvent decision, RiskEvaluation evaluation)
    {
        _riskState.TradesApproved++;
        _riskState.LastUpdate = Timestamp.FromDateTime(DateTime.UtcNow);

        // 计算实际止损止盈价格
        var currentPrice = decision.SuggestedPrice > 0 ? decision.SuggestedPrice : 0;
        var stopLoss = decision.Direction == "BUY" 
            ? currentPrice * (1 - evaluation.StopLossPct / 100)
            : currentPrice * (1 + evaluation.StopLossPct / 100);
        var takeProfit = decision.Direction == "BUY"
            ? currentPrice * (1 + evaluation.TakeProfitPct / 100)
            : currentPrice * (1 - evaluation.TakeProfitPct / 100);

        var approved = new ApprovedTradeEvent
        {
            DecisionId = decision.DecisionId,
            Symbol = decision.Symbol,
            Side = decision.Direction.ToLower(),
            OrderType = "market",
            Quantity = CalculateQuantity(decision.Symbol, evaluation.AdjustedPositionPct),
            Price = currentPrice,
            StopLoss = stopLoss,
            TakeProfit = takeProfit,
            RiskAssessment = evaluation.RiskLevel,
            PositionSizeAdjusted = evaluation.AdjustedPositionPct,
            RiskNotes = evaluation.Summary,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        await PublishAsync(approved);

        Logger.LogInformation(
            "[RiskManager] Trade APPROVED: {DecisionId}, Risk={Risk}, Position={Pos}%",
            decision.DecisionId, evaluation.RiskLevel, evaluation.AdjustedPositionPct);
    }

    private async Task RejectTrade(
        TradingDecisionEvent decision, 
        List<string> violations, 
        string riskLevel)
    {
        _riskState.TradesRejected++;
        _riskState.LastUpdate = Timestamp.FromDateTime(DateTime.UtcNow);

        var rejected = new TradeRejectedEvent
        {
            DecisionId = decision.DecisionId,
            Symbol = decision.Symbol,
            OriginalDirection = decision.Direction,
            RejectionReason = string.Join("; ", violations),
            RiskLevel = riskLevel,
            ViolatedRules = { violations },
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        await PublishAsync(rejected);

        Logger.LogWarning(
            "[RiskManager] Trade REJECTED: {DecisionId}, Reasons: {Reasons}",
            decision.DecisionId, string.Join(", ", violations));
    }

    private double CalculateQuantity(string symbol, double positionPct)
    {
        // 简化计算：根据仓位百分比和总权益计算数量
        var positionValue = _riskState.TotalEquity * (positionPct / 100);
        // 实际应该除以当前价格，这里返回 USDT 价值
        return positionValue;
    }

    /// <summary>
    /// 触发熔断
    /// </summary>
    public async Task TriggerCircuitBreakerAsync(string reason)
    {
        _riskState.IsTradingAllowed = false;
        _riskState.CircuitBreakerStatus = reason;

        await PublishAsync(new CircuitBreakerTriggeredEvent
        {
            TriggerType = reason,
            Description = $"风控熔断触发: {reason}",
            CooldownMinutes = _cooldownMinutes,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        Logger.LogWarning("[RiskManager] Circuit breaker triggered: {Reason}", reason);

        // 设置冷却定时器
        _ = Task.Delay(TimeSpan.FromMinutes(_cooldownMinutes)).ContinueWith(_ =>
        {
            _riskState.IsTradingAllowed = true;
            _riskState.CircuitBreakerStatus = "NORMAL";
            _riskState.ConsecutiveLosses = 0;
            Logger.LogInformation("[RiskManager] Circuit breaker reset");
        });
    }

    // ============ Helper Types ============

    private record RiskEvaluation
    {
        public bool Approved { get; init; }
        public string RiskLevel { get; init; } = "MEDIUM";
        public double AdjustedPositionPct { get; init; }
        public double StopLossPct { get; init; }
        public double TakeProfitPct { get; init; }
        public List<string> ViolatedRules { get; init; } = new();
        public string RiskNotes { get; init; } = "";
        public string Summary { get; init; } = "";
    }
}
