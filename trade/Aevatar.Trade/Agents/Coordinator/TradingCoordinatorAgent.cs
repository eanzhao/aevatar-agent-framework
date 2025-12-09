using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Core;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Trade.Agents.Coordinator;

/// <summary>
/// 交易协调 Agent (首席交易决策者)
/// 职责：综合各分析师的意见，做出最终交易决策
/// </summary>
public class TradingCoordinatorAgent : AIGAgentBase
{
    // ============ AI Configuration ============

    public override string SystemPrompt { get; set; } = """
        你是首席交易决策者，负责综合各方分析做出最终交易决策。

        【你的分析团队】
        1. 市场情绪分析师 - 提供市场情绪评分和趋势判断
        2. 技术分析师 - 提供技术指标分析和趋势判断
        3. 新闻分析师 - 提供新闻事件影响评估

        【决策框架】

        1. 信号一致性判断
           - 三方一致看多: 强烈买入信号
           - 三方一致看空: 强烈卖出信号
           - 两方看多一方中性: 温和买入
           - 两方看空一方中性: 温和卖出
           - 分析存在分歧: 偏向保守，观望为主

        2. 优先级规则
           - 重大新闻事件 > 技术分析 > 情绪分析
           - 极端情绪（恐慌/贪婪）时考虑逆向操作
           - 趋势明确时跟随趋势，震荡时观望

        3. 置信度调整
           - 多方信号共振: 提高置信度
           - 信号背离: 降低置信度
           - 近期准确率: 动态调整

        【输出格式】
        请严格按以下 JSON 格式输出：
        {
            "direction": "<BUY|SELL|HOLD>",
            "confidence": <1-100的整数>,
            "position_pct": <建议仓位百分比，0-30>,
            "reasoning": {
                "sentiment_factor": "<情绪面因素总结>",
                "technical_factor": "<技术面因素总结>",
                "news_factor": "<新闻面因素总结>",
                "final_logic": "<最终决策逻辑>"
            },
            "summary": "<一句话决策总结>"
        }

        【风险意识】
        - 不确定时选择观望
        - 避免追涨杀跌
        - 控制单笔仓位
        - 留有余地应对意外
        """;

    // ============ State ============

    private readonly CoordinatorState _coordState = new();
    
    // 配置
    private int _minConfidenceToTrade = 60;
    private double _sentimentWeight = 0.3;
    private double _technicalWeight = 0.4;
    private double _newsWeight = 0.3;

    // ============ Lifecycle ============

    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        _coordState.AgentId = Id.ToString();
        Logger.LogInformation("[Coordinator] Activated: {AgentId}", _coordState.AgentId);
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult(
            $"TradingCoordinator: Decisions={_coordState.DecisionsMade}, " +
            $"Approved={_coordState.DecisionsApproved}, " +
            $"Rejected={_coordState.DecisionsRejected}");
    }

    /// <summary>
    /// 配置决策参数
    /// </summary>
    public void Configure(
        int minConfidence = 60,
        double sentimentWeight = 0.3,
        double technicalWeight = 0.4,
        double newsWeight = 0.3)
    {
        _minConfidenceToTrade = minConfidence;
        _sentimentWeight = sentimentWeight;
        _technicalWeight = technicalWeight;
        _newsWeight = newsWeight;

        Logger.LogInformation(
            "[Coordinator] Configured: MinConf={MinConf}, Weights=[S:{S}, T:{T}, N:{N}]",
            minConfidence, sentimentWeight, technicalWeight, newsWeight);
    }

    // ============ Event Handlers ============

    /// <summary>
    /// 处理市场情绪分析结果
    /// </summary>
    [EventHandler]
    public async Task HandleSentimentAnalysis(MarketSentimentAnalysisEvent evt)
    {
        _coordState.LatestSentiment = evt;
        Logger.LogDebug(
            "[Coordinator] Received sentiment: Score={Score}, Trend={Trend}",
            evt.SentimentScore, evt.SentimentTrend);

        await TryMakeDecisionAsync();
    }

    /// <summary>
    /// 处理技术分析结果
    /// </summary>
    [EventHandler]
    public async Task HandleTechnicalAnalysis(TechnicalAnalysisEvent evt)
    {
        _coordState.LatestTechnical = evt;
        Logger.LogDebug(
            "[Coordinator] Received technical: Trend={Trend}, Signal={Signal}",
            evt.TrendDirection, evt.Signal);

        await TryMakeDecisionAsync();
    }

    /// <summary>
    /// 处理新闻影响分析结果
    /// </summary>
    [EventHandler]
    public async Task HandleNewsAnalysis(NewsImpactAnalysisEvent evt)
    {
        _coordState.LatestNews = evt;
        Logger.LogDebug(
            "[Coordinator] Received news: Impact={Impact}, Level={Level}",
            evt.ImpactType, evt.ImpactLevel);

        await TryMakeDecisionAsync();
    }

    /// <summary>
    /// 处理交易批准事件（统计用）
    /// </summary>
    [EventHandler]
    public Task HandleTradeApproved(ApprovedTradeEvent evt)
    {
        _coordState.DecisionsApproved++;
        Logger.LogInformation(
            "[Coordinator] Decision approved: {DecisionId} -> {Side} {Symbol}",
            evt.DecisionId, evt.Side, evt.Symbol);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 处理交易拒绝事件（统计用）
    /// </summary>
    [EventHandler]
    public Task HandleTradeRejected(TradeRejectedEvent evt)
    {
        _coordState.DecisionsRejected++;
        Logger.LogWarning(
            "[Coordinator] Decision rejected: {DecisionId}, Reason: {Reason}",
            evt.DecisionId, evt.RejectionReason);
        return Task.CompletedTask;
    }

    // ============ Decision Logic ============

    /// <summary>
    /// 尝试做出交易决策
    /// </summary>
    private async Task TryMakeDecisionAsync()
    {
        // 检查是否有足够的分析数据
        if (!HasSufficientData())
        {
            Logger.LogDebug("[Coordinator] Insufficient data for decision");
            return;
        }

        // 检查决策间隔（避免过于频繁）
        if (_coordState.LastDecisionTime != null)
        {
            var elapsed = DateTime.UtcNow - _coordState.LastDecisionTime.ToDateTime();
            if (elapsed.TotalSeconds < 30)
            {
                Logger.LogDebug("[Coordinator] Decision throttled, last decision {Seconds}s ago",
                    elapsed.TotalSeconds);
                return;
            }
        }

        await MakeDecisionAsync();
    }

    private bool HasSufficientData()
    {
        // 至少需要技术分析和情绪分析
        if (_coordState.LatestTechnical == null) return false;
        if (_coordState.LatestSentiment == null) return false;

        // 检查数据时效性（5分钟内）
        var now = DateTime.UtcNow;
        var techAge = now - _coordState.LatestTechnical.Timestamp.ToDateTime();
        var sentAge = now - _coordState.LatestSentiment.Timestamp.ToDateTime();

        return techAge.TotalMinutes < 5 && sentAge.TotalMinutes < 5;
    }

    private async Task MakeDecisionAsync()
    {
        var symbol = _coordState.LatestTechnical!.Symbol;
        var prompt = BuildDecisionPrompt();

        try
        {
            var response = await CompleteAsync(prompt);
            var decision = ParseDecisionResponse(response, symbol);

            // 更新状态
            _coordState.LastDecision = decision.Direction;
            _coordState.DecisionsMade++;
            _coordState.LastDecisionTime = Timestamp.FromDateTime(DateTime.UtcNow);

            // 检查置信度阈值
            if (decision.Direction == "HOLD" || decision.Confidence < _minConfidenceToTrade)
            {
                Logger.LogInformation(
                    "[Coordinator] Decision: HOLD (Confidence={Conf} < {Min} or explicit HOLD)",
                    decision.Confidence, _minConfidenceToTrade);
                return;
            }

            // 发布交易决策
            await PublishAsync(decision);

            Logger.LogInformation(
                "[Coordinator] Decision published: {Direction} {Symbol}, Confidence={Conf}%",
                decision.Direction, symbol, decision.Confidence);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[Coordinator] Decision failed for {Symbol}", symbol);
        }
    }

    private string BuildDecisionPrompt()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("请综合以下分析报告，做出交易决策：");
        sb.AppendLine();

        // 情绪分析
        if (_coordState.LatestSentiment != null)
        {
            var s = _coordState.LatestSentiment;
            sb.AppendLine("【市场情绪分析师报告】");
            sb.AppendLine($"- 情绪评分: {s.SentimentScore} (-100~+100)");
            sb.AppendLine($"- 情绪趋势: {s.SentimentTrend}");
            sb.AppendLine($"- 恐慌贪婪指数: {s.FearGreedIndex}");
            sb.AppendLine($"- 多空比: {s.LongShortRatio:F2}");
            sb.AppendLine($"- 资金费率: {s.FundingRate:F4}%");
            sb.AppendLine($"- 分析总结: {s.AnalysisSummary}");
            sb.AppendLine($"- 置信度: {s.Confidence}%");
            sb.AppendLine();
        }

        // 技术分析
        if (_coordState.LatestTechnical != null)
        {
            var t = _coordState.LatestTechnical;
            sb.AppendLine("【技术分析师报告】");
            sb.AppendLine($"- 趋势方向: {t.TrendDirection}");
            sb.AppendLine($"- 趋势强度: {t.TrendStrength}/10");
            sb.AppendLine($"- 交易信号: {t.Signal}");
            sb.AppendLine($"- RSI: {t.Rsi:F2}");
            sb.AppendLine($"- MACD: {t.Macd:F4}");
            sb.AppendLine($"- 支撑位: {t.SupportLevel:F2}");
            sb.AppendLine($"- 阻力位: {t.ResistanceLevel:F2}");
            if (!string.IsNullOrEmpty(t.PatternDetected))
                sb.AppendLine($"- 形态识别: {t.PatternDetected}");
            sb.AppendLine($"- 分析总结: {t.AnalysisSummary}");
            sb.AppendLine($"- 置信度: {t.Confidence}%");
            sb.AppendLine();
        }

        // 新闻分析
        if (_coordState.LatestNews != null)
        {
            var n = _coordState.LatestNews;
            sb.AppendLine("【新闻分析师报告】");
            sb.AppendLine($"- 新闻标题: {n.Headline}");
            sb.AppendLine($"- 影响类型: {n.ImpactType}");
            sb.AppendLine($"- 影响程度: {n.ImpactLevel}");
            sb.AppendLine($"- 影响时效: {n.ImpactDuration}");
            sb.AppendLine($"- 分析总结: {n.AnalysisSummary}");
            sb.AppendLine($"- 置信度: {n.Confidence}%");
            sb.AppendLine();
        }

        sb.AppendLine("【决策权重配置】");
        sb.AppendLine($"- 情绪分析权重: {_sentimentWeight * 100}%");
        sb.AppendLine($"- 技术分析权重: {_technicalWeight * 100}%");
        sb.AppendLine($"- 新闻分析权重: {_newsWeight * 100}%");
        sb.AppendLine($"- 最小交易置信度: {_minConfidenceToTrade}%");

        return sb.ToString();
    }

    private TradingDecisionEvent ParseDecisionResponse(string response, string symbol)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(response);
            var root = doc.RootElement;

            var reasoning = "";
            if (root.TryGetProperty("reasoning", out var reasoningObj))
            {
                var parts = new List<string>();
                if (reasoningObj.TryGetProperty("sentiment_factor", out var sf))
                    parts.Add($"情绪面: {sf.GetString()}");
                if (reasoningObj.TryGetProperty("technical_factor", out var tf))
                    parts.Add($"技术面: {tf.GetString()}");
                if (reasoningObj.TryGetProperty("news_factor", out var nf))
                    parts.Add($"新闻面: {nf.GetString()}");
                if (reasoningObj.TryGetProperty("final_logic", out var fl))
                    parts.Add($"决策逻辑: {fl.GetString()}");
                reasoning = string.Join(" | ", parts);
            }

            return new TradingDecisionEvent
            {
                DecisionId = Guid.NewGuid().ToString("N")[..16],
                Symbol = symbol,
                Direction = root.TryGetProperty("direction", out var dir)
                    ? dir.GetString() ?? "HOLD" : "HOLD",
                Confidence = root.TryGetProperty("confidence", out var conf)
                    ? conf.GetInt32() : 50,
                SuggestedPositionPct = root.TryGetProperty("position_pct", out var pos)
                    ? pos.GetDouble() : 10,
                SentimentSummary = _coordState.LatestSentiment?.AnalysisSummary ?? "",
                TechnicalSummary = _coordState.LatestTechnical?.AnalysisSummary ?? "",
                NewsSummary = _coordState.LatestNews?.AnalysisSummary ?? "",
                Reasoning = reasoning,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }
        catch
        {
            return new TradingDecisionEvent
            {
                DecisionId = Guid.NewGuid().ToString("N")[..16],
                Symbol = symbol,
                Direction = "HOLD",
                Confidence = 30,
                Reasoning = response,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }
    }
}
