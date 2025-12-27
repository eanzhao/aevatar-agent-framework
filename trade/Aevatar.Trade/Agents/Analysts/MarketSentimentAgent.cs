using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Trade.Agents.Analysts;

/// <summary>
/// Market sentiment analysis agent
/// Responsibilities: Analyze market sentiment indicators and judge overall market atmosphere
/// </summary>
public class MarketSentimentAgent : AIGAgentBase
{
    // ============ AI Configuration ============

    public override string SystemPrompt { get; set; } = """
        You are a senior cryptocurrency market sentiment analyst with extensive knowledge of market psychology and behavioral finance.

        Your task is to judge current market sentiment based on the following market data:

        【Analysis Dimensions】
        1. Fear & Greed Index (0-100)
           - 0-25: Extreme fear → Possible buying opportunity
           - 25-45: Fear
           - 45-55: Neutral
           - 55-75: Greed
           - 75-100: Extreme greed → Possible sell signal

        2. Long/Short Ratio
           - < 0.8: Bears dominate, market bearish
           - 0.8-1.2: Long/short balanced
           - > 1.2: Bulls dominate, market bullish
           - Extreme values may indicate reversal

        3. Funding Rate
           - Positive: Longs pay shorts, bullish sentiment high
           - Negative: Shorts pay longs, bearish sentiment high
           - Extreme positive (>0.1%): Possibly overheated
           - Extreme negative (<-0.05%): Possibly oversold

        4. Open Interest Changes
           - Rising + Price rising: Bulls actively building positions
           - Rising + Price falling: Bears actively building positions
           - Falling: Positions closing, trend may weaken

        【Output Format】
        Please strictly output in the following JSON format:
        {
            "sentiment_score": <integer from -100 to +100>,
            "sentiment_trend": "<UP|DOWN|SIDEWAYS>",
            "signal": "<BULLISH|BEARISH|NEUTRAL>",
            "confidence": <integer from 1-100>,
            "key_observations": ["Observation 1", "Observation 2", "Observation 3"],
            "summary": "<One-sentence summary of current sentiment state>"
        }

        【Notes】
        - Extreme sentiment often indicates reversal
        - Pay attention to divergence between sentiment and price
        - Signals are more reliable when multiple indicators resonate
        """;

    // ============ State ============

    private readonly SentimentAnalystState _sentimentState = new();

    // ============ Lifecycle ============

    protected override async Task OnActivateAsync(CancellationToken ct = default)
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
    /// Handle market data and periodically trigger sentiment analysis
    /// </summary>
    [EventHandler]
    public async Task HandleMarketTick(MarketTickEvent evt)
    {
        // Analyze every N ticks to avoid being too frequent
        if (_sentimentState.AnalysisCount % 10 != 0 && _sentimentState.AnalysisCount > 0)
        {
            return;
        }

        await AnalyzeSentimentAsync(evt.Symbol, evt);
    }

    // ============ Analysis Methods ============

    /// <summary>
    /// Execute sentiment analysis
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
            var chat = await ChatAsync(ChatRequest.Create(prompt));
            var analysis = ParseAnalysisResponse(chat.Content ?? string.Empty, symbol);

            // Update state
            _sentimentState.CurrentSentiment = analysis.SentimentScore;
            _sentimentState.AnalysisCount++;
            _sentimentState.LastAnalysis = Timestamp.FromDateTime(DateTime.UtcNow);

            // Keep historical records
            if (_sentimentState.SentimentHistory.Count >= 100)
                _sentimentState.SentimentHistory.RemoveAt(0);
            _sentimentState.SentimentHistory.Add(analysis.SentimentScore);

            // Publish analysis results
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
        sb.AppendLine($"Please analyze the market sentiment for {symbol}:");
        sb.AppendLine();

        if (tick != null)
        {
            sb.AppendLine("【Current Market Data】");
            sb.AppendLine($"- Price: {tick.Price:F2}");
            sb.AppendLine($"- 24h Change: {tick.Change24H:F2}%");
            sb.AppendLine($"- 24h Volume: {tick.Volume24H:F0}");
            sb.AppendLine();
        }

        sb.AppendLine("【Sentiment Indicators】");
        sb.AppendLine($"- Fear & Greed Index: {fearGreedIndex ?? 50}");
        sb.AppendLine($"- Long/Short Ratio: {longShortRatio ?? 1.0:F2}");
        sb.AppendLine($"- Funding Rate: {(fundingRate ?? 0) * 100:F4}%");
        sb.AppendLine($"- Open Interest: {openInterest ?? 0:F0}");

        return sb.ToString();
    }

    private MarketSentimentAnalysisEvent ParseAnalysisResponse(string response, string symbol)
    {
        // Try to parse JSON response
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
            // If parsing fails, return default values
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
