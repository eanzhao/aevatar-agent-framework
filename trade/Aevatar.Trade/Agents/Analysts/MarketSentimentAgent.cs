using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Core;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Trade.Agents.Analysts;

/// <summary>
/// 市场情绪分析 Agent
/// 职责：分析市场情绪指标，判断市场整体氛围
/// </summary>
public class MarketSentimentAgent : AIGAgentBase
{
    // ============ AI Configuration ============

    public override string SystemPrompt { get; set; } = """
        你是一位资深的加密货币市场情绪分析师，拥有丰富的市场心理学和行为金融学知识。

        你的任务是根据以下市场数据判断当前市场情绪：

        【分析维度】
        1. 恐慌贪婪指数 (0-100)
           - 0-25: 极度恐慌 → 可能是买入机会
           - 25-45: 恐慌
           - 45-55: 中性
           - 55-75: 贪婪
           - 75-100: 极度贪婪 → 可能是卖出信号

        2. 多空比 (Long/Short Ratio)
           - < 0.8: 空头占优，市场偏空
           - 0.8-1.2: 多空均衡
           - > 1.2: 多头占优，市场偏多
           - 极端值可能预示反转

        3. 资金费率 (Funding Rate)
           - 正值: 多头支付空头，做多情绪高涨
           - 负值: 空头支付多头，做空情绪高涨
           - 极端正值 (>0.1%): 可能过热
           - 极端负值 (<-0.05%): 可能超卖

        4. 持仓量变化
           - 上升 + 价格上升: 多头积极建仓
           - 上升 + 价格下降: 空头积极建仓
           - 下降: 头寸平仓，趋势可能减弱

        【输出格式】
        请严格按以下 JSON 格式输出：
        {
            "sentiment_score": <-100到+100的整数>,
            "sentiment_trend": "<UP|DOWN|SIDEWAYS>",
            "signal": "<BULLISH|BEARISH|NEUTRAL>",
            "confidence": <1-100的整数>,
            "key_observations": ["观察1", "观察2", "观察3"],
            "summary": "<一句话总结当前情绪状态>"
        }

        【注意事项】
        - 极端情绪往往预示反转
        - 关注情绪与价格的背离
        - 多个指标共振时信号更可靠
        """;

    // ============ State ============

    private readonly SentimentAnalystState _sentimentState = new();

    // ============ Lifecycle ============

    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        _sentimentState.AgentId = Id.ToString();
        Logger.LogInformation("[SentimentAgent] Activated: {AgentId}", _sentimentState.AgentId);
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult(
            $"MarketSentimentAgent: Score={_sentimentState.CurrentSentiment}, " +
            $"Analyses={_sentimentState.AnalysisCount}");
    }

    // ============ Event Handlers ============

    /// <summary>
    /// 处理行情数据，定期触发情绪分析
    /// </summary>
    [EventHandler]
    public async Task HandleMarketTick(MarketTickEvent evt)
    {
        // 每 N 个 tick 分析一次，避免过于频繁
        if (_sentimentState.AnalysisCount % 10 != 0 && _sentimentState.AnalysisCount > 0)
        {
            return;
        }

        await AnalyzeSentimentAsync(evt.Symbol, evt);
    }

    // ============ Analysis Methods ============

    /// <summary>
    /// 执行情绪分析
    /// </summary>
    public async Task AnalyzeSentimentAsync(
        string symbol,
        MarketTickEvent? latestTick = null,
        double? fearGreedIndex = null,
        double? longShortRatio = null,
        double? fundingRate = null,
        double? openInterest = null)
    {
        var prompt = BuildAnalysisPrompt(
            symbol, latestTick, fearGreedIndex, longShortRatio, fundingRate, openInterest);

        try
        {
            var response = await CompleteAsync(prompt);
            var analysis = ParseAnalysisResponse(response, symbol);

            // 更新状态
            _sentimentState.CurrentSentiment = analysis.SentimentScore;
            _sentimentState.AnalysisCount++;
            _sentimentState.LastAnalysis = Timestamp.FromDateTime(DateTime.UtcNow);

            // 保留历史记录
            if (_sentimentState.SentimentHistory.Count >= 100)
                _sentimentState.SentimentHistory.RemoveAt(0);
            _sentimentState.SentimentHistory.Add(analysis.SentimentScore);

            // 发布分析结果
            await PublishAsync(analysis);

            Logger.LogInformation(
                "[SentimentAgent] Analysis completed for {Symbol}: Score={Score}, Signal={Signal}",
                symbol, analysis.SentimentScore, analysis.AnalysisSummary);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[SentimentAgent] Analysis failed for {Symbol}", symbol);
        }
    }

    // ============ Private Methods ============

    private string BuildAnalysisPrompt(
        string symbol,
        MarketTickEvent? tick,
        double? fearGreedIndex,
        double? longShortRatio,
        double? fundingRate,
        double? openInterest)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"请分析 {symbol} 的市场情绪：");
        sb.AppendLine();

        if (tick != null)
        {
            sb.AppendLine("【当前行情】");
            sb.AppendLine($"- 价格: {tick.Price:F2}");
            sb.AppendLine($"- 24h涨跌: {tick.Change24H:F2}%");
            sb.AppendLine($"- 24h成交量: {tick.Volume24H:F0}");
            sb.AppendLine();
        }

        sb.AppendLine("【情绪指标】");
        sb.AppendLine($"- 恐慌贪婪指数: {fearGreedIndex ?? 50}");
        sb.AppendLine($"- 多空比: {longShortRatio ?? 1.0:F2}");
        sb.AppendLine($"- 资金费率: {(fundingRate ?? 0) * 100:F4}%");
        sb.AppendLine($"- 持仓量: {openInterest ?? 0:F0}");

        return sb.ToString();
    }

    private MarketSentimentAnalysisEvent ParseAnalysisResponse(string response, string symbol)
    {
        // 尝试解析 JSON 响应
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(response);
            var root = doc.RootElement;

            return new MarketSentimentAnalysisEvent
            {
                Symbol = symbol,
                SentimentScore = root.TryGetProperty("sentiment_score", out var score) 
                    ? score.GetInt32() : 0,
                SentimentTrend = root.TryGetProperty("sentiment_trend", out var trend) 
                    ? trend.GetString() ?? "SIDEWAYS" : "SIDEWAYS",
                Confidence = root.TryGetProperty("confidence", out var conf) 
                    ? conf.GetInt32() : 50,
                AnalysisSummary = root.TryGetProperty("summary", out var summary) 
                    ? summary.GetString() ?? "" : response,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }
        catch
        {
            // 如果解析失败，返回默认值
            return new MarketSentimentAnalysisEvent
            {
                Symbol = symbol,
                SentimentScore = 0,
                SentimentTrend = "SIDEWAYS",
                Confidence = 30,
                AnalysisSummary = response,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }
    }
}
