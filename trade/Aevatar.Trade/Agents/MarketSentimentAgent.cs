using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Core;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Trade.Agents;

/// <summary>
/// 市场情绪分析 Agent
/// 职责：分析市场情绪指标，判断市场整体氛围
/// 
/// 输入：MarketTickEvent (多空比、资金费率等衍生数据)
/// 输出：MarketSentimentAnalysisEvent
/// </summary>
public class MarketSentimentAgent : AIGAgentBase<SentimentAnalystState>
{
    // ============ AI Configuration ============

    public override string SystemPrompt => @"
你是一位资深的加密货币市场情绪分析师，拥有 10 年交易经验。

## 你的任务
根据提供的市场数据，分析当前市场情绪并给出评估。

## 分析维度
1. **恐慌贪婪指数** (权重 30%)
   - 0-25: 极度恐慌 → 可能是买入机会
   - 25-45: 恐慌
   - 45-55: 中性
   - 55-75: 贪婪
   - 75-100: 极度贪婪 → 可能是卖出信号

2. **多空比** (权重 25%)
   - < 0.8: 空头占优，市场悲观
   - 0.8-1.2: 多空平衡
   - > 1.2: 多头占优，市场乐观

3. **资金费率** (权重 25%)
   - 负费率: 空头支付多头，空头拥挤
   - 正费率 > 0.01%: 多头支付空头，多头拥挤
   - 极端费率 (> 0.05%): 警示信号

4. **持仓量变化** (权重 20%)
   - 持仓量上升 + 价格上升: 多头增仓，看涨
   - 持仓量下降 + 价格下降: 多头平仓，看跌
   - 持仓量上升 + 价格下降: 空头增仓，看跌

## 输出格式 (JSON)
{
  ""sentiment_score"": <-100 到 +100 的整数>,
  ""sentiment_trend"": ""UP"" | ""DOWN"" | ""SIDEWAYS"",
  ""confidence"": <1-100>,
  ""summary"": ""简要分析总结，50字以内""
}

## 注意事项
- 保持客观，不要过度解读
- 极端情绪往往意味着反转机会
- 多个指标冲突时，偏向保守判断
";

    public MarketSentimentAgent() { }
    
    public MarketSentimentAgent(Guid id) : base(id) { }

    public override Task<string> GetDescriptionAsync()
        => Task.FromResult($"市场情绪分析师 - 当前情绪: {State.CurrentSentiment}");

    // ============ Lifecycle ============

    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        State.AgentId = Id.ToString();
    }

    // ============ Event Handlers ============

    /// <summary>
    /// 处理行情数据，提取情绪指标
    /// </summary>
    [EventHandler]
    public async Task HandleMarketTick(MarketTickEvent evt)
    {
        // 累积数据，不是每个 tick 都分析
        // 可以基于时间间隔或数据变化幅度触发分析
        
        // 这里简化处理：每 N 个 tick 分析一次
        State.AnalysisCount++;
        
        if (State.AnalysisCount % 10 != 0) return;

        await AnalyzeMarketSentimentAsync(evt);
    }

    // ============ Analysis Logic ============

    private async Task AnalyzeMarketSentimentAsync(MarketTickEvent tick)
    {
        try
        {
            // 构建分析上下文
            var context = $@"
## 当前市场数据
- 交易对: {tick.Symbol}
- 当前价格: ${tick.Price:F2}
- 24H 涨跌幅: {tick.Change24H:F2}%
- 24H 成交量: {tick.Volume24H:F0}
- 24H 最高: ${tick.High24H:F2}
- 24H 最低: ${tick.Low24H:F2}

## 情绪指标 (模拟数据，实际应从数据源获取)
- 恐慌贪婪指数: 45
- 多空比: 0.95
- 资金费率: 0.01%
- 持仓量变化: +2.5%

请分析当前市场情绪。
";

            // 调用 LLM 分析
            var response = await InvokeAIAsync(context);
            
            // 解析响应
            var analysis = ParseSentimentResponse(response, tick.Symbol);
            
            // 更新状态
            State.CurrentSentiment = analysis.SentimentScore;
            State.SentimentHistory.Add(analysis.SentimentScore);
            if (State.SentimentHistory.Count > 100)
                State.SentimentHistory.RemoveAt(0);
            State.LastAnalysis = Timestamp.FromDateTime(DateTime.UtcNow);

            // 发布分析结果
            await PublishAsync(analysis, EventDirection.Up);

            Logger.LogInformation(
                "Sentiment analysis: Score={Score}, Trend={Trend}, Confidence={Confidence}",
                analysis.SentimentScore,
                analysis.SentimentTrend,
                analysis.Confidence);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to analyze market sentiment");
        }
    }

    private MarketSentimentAnalysisEvent ParseSentimentResponse(string response, string symbol)
    {
        // 尝试解析 JSON 响应
        try
        {
            // 简化解析，实际应使用 JSON 反序列化
            var score = ExtractIntValue(response, "sentiment_score", 0);
            var trend = ExtractStringValue(response, "sentiment_trend", "SIDEWAYS");
            var confidence = ExtractIntValue(response, "confidence", 50);
            var summary = ExtractStringValue(response, "summary", "分析完成");

            return new MarketSentimentAnalysisEvent
            {
                Symbol = symbol,
                SentimentScore = score,
                SentimentTrend = trend,
                FearGreedIndex = Math.Abs(score) / 2.0 + 50, // 转换到 0-100
                LongShortRatio = score > 0 ? 1.0 + score / 200.0 : 1.0 - Math.Abs(score) / 200.0,
                FundingRate = 0.01,
                OpenInterest = 0,
                AnalysisSummary = summary,
                Confidence = confidence,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }
        catch
        {
            // 解析失败，返回中性结果
            return new MarketSentimentAnalysisEvent
            {
                Symbol = symbol,
                SentimentScore = 0,
                SentimentTrend = "SIDEWAYS",
                FearGreedIndex = 50,
                LongShortRatio = 1.0,
                AnalysisSummary = "分析结果解析失败，返回中性判断",
                Confidence = 30,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }
    }

    private static int ExtractIntValue(string json, string key, int defaultValue)
    {
        var pattern = $"\"{key}\"\\s*:\\s*(-?\\d+)";
        var match = System.Text.RegularExpressions.Regex.Match(json, pattern);
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) 
            ? value 
            : defaultValue;
    }

    private static string ExtractStringValue(string json, string key, string defaultValue)
    {
        var pattern = $"\"{key}\"\\s*:\\s*\"([^\"]+)\"";
        var match = System.Text.RegularExpressions.Regex.Match(json, pattern);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}
