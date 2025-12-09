using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Core;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Trade.Agents.Analysts;

/// <summary>
/// 技术分析 Agent
/// 职责：基于价格数据进行技术分析，识别趋势和交易信号
/// </summary>
public class TechnicalAnalystAgent : AIGAgentBase
{
    // ============ AI Configuration ============

    public override string SystemPrompt { get; set; } = """
        你是一位专业的加密货币技术分析师，精通各种技术指标和图表形态分析。

        【技术指标解读】

        1. RSI (相对强弱指数, 14周期)
           - < 30: 超卖区，可能反弹
           - 30-70: 正常区间
           - > 70: 超买区，可能回调
           - RSI 背离是重要信号

        2. MACD
           - MACD > Signal: 多头动能
           - MACD < Signal: 空头动能
           - 金叉: MACD 上穿 Signal，买入信号
           - 死叉: MACD 下穿 Signal，卖出信号
           - 柱状图扩大: 趋势加强
           - 柱状图收缩: 趋势减弱

        3. 均线系统
           - 价格 > MA20 > MA60: 多头排列
           - 价格 < MA20 < MA60: 空头排列
           - 均线交叉: 趋势转变信号

        4. 布林带
           - 触及上轨: 可能超买
           - 触及下轨: 可能超卖
           - 带宽收窄: 即将突破
           - 带宽扩张: 波动加大

        5. ATR (平均真实波幅)
           - 用于设置止损距离
           - ATR 扩大: 波动增加
           - ATR 收缩: 波动减小

        【输出格式】
        请严格按以下 JSON 格式输出：
        {
            "trend_direction": "<BULLISH|BEARISH|SIDEWAYS>",
            "trend_strength": <1-10的整数>,
            "signal": "<BUY|SELL|HOLD>",
            "support_level": <支撑价格>,
            "resistance_level": <阻力价格>,
            "pattern_detected": "<检测到的形态，如无则为空>",
            "confidence": <1-100的整数>,
            "key_observations": ["观察1", "观察2", "观察3"],
            "summary": "<一句话总结技术面状况>"
        }

        【注意事项】
        - 多个指标共振时信号更可靠
        - 注意指标与价格的背离
        - 大周期趋势优先于小周期信号
        - 关键支撑阻力位的突破确认
        """;

    // ============ State ============

    private readonly TechnicalAnalystState _techState = new();
    private readonly List<KlineUpdateEvent> _klineBuffer = new();
    private const int KlineBufferSize = 200;

    // ============ Lifecycle ============

    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        _techState.AgentId = Id.ToString();
        Logger.LogInformation("[TechnicalAgent] Activated: {AgentId}", _techState.AgentId);
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult(
            $"TechnicalAnalystAgent: Trend={_techState.CurrentTrend}, " +
            $"Strength={_techState.TrendStrength}, " +
            $"Analyses={_techState.AnalysisCount}");
    }

    // ============ Event Handlers ============

    /// <summary>
    /// 处理K线数据，更新缓冲区并触发分析
    /// </summary>
    [EventHandler]
    public async Task HandleKlineUpdate(KlineUpdateEvent evt)
    {
        // 更新K线缓冲区
        _klineBuffer.Add(evt);
        if (_klineBuffer.Count > KlineBufferSize)
            _klineBuffer.RemoveAt(0);

        // 每收到 N 根K线分析一次
        if (_klineBuffer.Count % 5 == 0 && _klineBuffer.Count >= 60)
        {
            await AnalyzeTechnicalAsync(evt.Symbol);
        }
    }

    // ============ Analysis Methods ============

    /// <summary>
    /// 执行技术分析
    /// </summary>
    public async Task AnalyzeTechnicalAsync(string symbol)
    {
        if (_klineBuffer.Count < 60)
        {
            Logger.LogDebug("[TechnicalAgent] Insufficient data for analysis");
            return;
        }

        // 计算技术指标
        var indicators = CalculateIndicators();
        var prompt = BuildAnalysisPrompt(symbol, indicators);

        try
        {
            var response = await CompleteAsync(prompt);
            var analysis = ParseAnalysisResponse(response, symbol, indicators);

            // 更新状态
            _techState.CurrentTrend = analysis.TrendDirection;
            _techState.TrendStrength = analysis.TrendStrength;
            _techState.LastRsi = analysis.Rsi;
            _techState.LastMacd = analysis.Macd;
            _techState.AnalysisCount++;
            _techState.LastAnalysis = Timestamp.FromDateTime(DateTime.UtcNow);

            // 发布分析结果
            await PublishAsync(analysis);

            Logger.LogInformation(
                "[TechnicalAgent] Analysis completed for {Symbol}: Trend={Trend}, Signal={Signal}",
                symbol, analysis.TrendDirection, analysis.Signal);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[TechnicalAgent] Analysis failed for {Symbol}", symbol);
        }
    }

    // ============ Technical Indicators ============

    private TechnicalIndicators CalculateIndicators()
    {
        var closes = _klineBuffer.Select(k => k.Close).ToList();
        var highs = _klineBuffer.Select(k => k.High).ToList();
        var lows = _klineBuffer.Select(k => k.Low).ToList();

        return new TechnicalIndicators
        {
            CurrentPrice = closes.Last(),
            RSI = CalculateRSI(closes, 14),
            MACD = CalculateMACD(closes),
            MA20 = CalculateSMA(closes, 20),
            MA60 = CalculateSMA(closes, 60),
            BollingerUpper = CalculateBollingerUpper(closes, 20, 2),
            BollingerLower = CalculateBollingerLower(closes, 20, 2),
            ATR = CalculateATR(highs, lows, closes, 14),
            Support = FindSupport(lows),
            Resistance = FindResistance(highs)
        };
    }

    private double CalculateRSI(List<double> closes, int period)
    {
        if (closes.Count < period + 1) return 50;

        var gains = new List<double>();
        var losses = new List<double>();

        for (int i = closes.Count - period; i < closes.Count; i++)
        {
            var change = closes[i] - closes[i - 1];
            gains.Add(Math.Max(0, change));
            losses.Add(Math.Max(0, -change));
        }

        var avgGain = gains.Average();
        var avgLoss = losses.Average();

        if (avgLoss == 0) return 100;
        var rs = avgGain / avgLoss;
        return 100 - (100 / (1 + rs));
    }

    private (double MACD, double Signal, double Histogram) CalculateMACD(List<double> closes)
    {
        var ema12 = CalculateEMA(closes, 12);
        var ema26 = CalculateEMA(closes, 26);
        var macd = ema12 - ema26;

        // 简化: Signal 使用 MACD 的 9 周期 EMA
        var signal = macd * 0.9; // 简化计算
        var histogram = macd - signal;

        return (macd, signal, histogram);
    }

    private double CalculateSMA(List<double> values, int period)
    {
        if (values.Count < period) return values.Average();
        return values.Skip(values.Count - period).Average();
    }

    private double CalculateEMA(List<double> values, int period)
    {
        if (values.Count < period) return values.Average();

        var multiplier = 2.0 / (period + 1);
        var ema = values.Take(period).Average();

        foreach (var value in values.Skip(period))
        {
            ema = (value - ema) * multiplier + ema;
        }

        return ema;
    }

    private double CalculateBollingerUpper(List<double> closes, int period, double stdDevMult)
    {
        var sma = CalculateSMA(closes, period);
        var stdDev = CalculateStdDev(closes.TakeLast(period).ToList());
        return sma + stdDevMult * stdDev;
    }

    private double CalculateBollingerLower(List<double> closes, int period, double stdDevMult)
    {
        var sma = CalculateSMA(closes, period);
        var stdDev = CalculateStdDev(closes.TakeLast(period).ToList());
        return sma - stdDevMult * stdDev;
    }

    private double CalculateStdDev(List<double> values)
    {
        var avg = values.Average();
        var sumSquares = values.Sum(v => Math.Pow(v - avg, 2));
        return Math.Sqrt(sumSquares / values.Count);
    }

    private double CalculateATR(List<double> highs, List<double> lows, List<double> closes, int period)
    {
        if (highs.Count < period + 1) return 0;

        var trueRanges = new List<double>();
        for (int i = highs.Count - period; i < highs.Count; i++)
        {
            var tr = Math.Max(
                highs[i] - lows[i],
                Math.Max(
                    Math.Abs(highs[i] - closes[i - 1]),
                    Math.Abs(lows[i] - closes[i - 1])));
            trueRanges.Add(tr);
        }

        return trueRanges.Average();
    }

    private double FindSupport(List<double> lows)
    {
        var recentLows = lows.TakeLast(20).ToList();
        return recentLows.Min();
    }

    private double FindResistance(List<double> highs)
    {
        var recentHighs = highs.TakeLast(20).ToList();
        return recentHighs.Max();
    }

    // ============ Helper Methods ============

    private string BuildAnalysisPrompt(string symbol, TechnicalIndicators ind)
    {
        return $"""
            请分析 {symbol} 的技术面：

            【当前价格】
            - 价格: {ind.CurrentPrice:F2}

            【技术指标】
            - RSI(14): {ind.RSI:F2}
            - MACD: {ind.MACD.MACD:F4}
            - MACD Signal: {ind.MACD.Signal:F4}
            - MACD Histogram: {ind.MACD.Histogram:F4}
            - MA20: {ind.MA20:F2}
            - MA60: {ind.MA60:F2}
            - 布林上轨: {ind.BollingerUpper:F2}
            - 布林下轨: {ind.BollingerLower:F2}
            - ATR(14): {ind.ATR:F2}

            【关键价位】
            - 支撑位: {ind.Support:F2}
            - 阻力位: {ind.Resistance:F2}

            【均线位置】
            - 价格 vs MA20: {(ind.CurrentPrice > ind.MA20 ? "上方" : "下方")}
            - 价格 vs MA60: {(ind.CurrentPrice > ind.MA60 ? "上方" : "下方")}
            - MA20 vs MA60: {(ind.MA20 > ind.MA60 ? "多头排列" : "空头排列")}
            """;
    }

    private TechnicalAnalysisEvent ParseAnalysisResponse(
        string response, 
        string symbol, 
        TechnicalIndicators ind)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(response);
            var root = doc.RootElement;

            return new TechnicalAnalysisEvent
            {
                Symbol = symbol,
                TrendDirection = root.TryGetProperty("trend_direction", out var trend)
                    ? trend.GetString() ?? "SIDEWAYS" : "SIDEWAYS",
                TrendStrength = root.TryGetProperty("trend_strength", out var strength)
                    ? strength.GetInt32() : 5,
                Signal = root.TryGetProperty("signal", out var signal)
                    ? signal.GetString() ?? "HOLD" : "HOLD",
                SupportLevel = ind.Support,
                ResistanceLevel = ind.Resistance,
                Rsi = ind.RSI,
                Macd = ind.MACD.MACD,
                MacdSignal = ind.MACD.Signal,
                MacdHistogram = ind.MACD.Histogram,
                Ma20 = ind.MA20,
                Ma60 = ind.MA60,
                BollingerUpper = ind.BollingerUpper,
                BollingerLower = ind.BollingerLower,
                Atr = ind.ATR,
                PatternDetected = root.TryGetProperty("pattern_detected", out var pattern)
                    ? pattern.GetString() ?? "" : "",
                Confidence = root.TryGetProperty("confidence", out var conf)
                    ? conf.GetInt32() : 50,
                AnalysisSummary = root.TryGetProperty("summary", out var summary)
                    ? summary.GetString() ?? "" : response,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }
        catch
        {
            return new TechnicalAnalysisEvent
            {
                Symbol = symbol,
                TrendDirection = "SIDEWAYS",
                TrendStrength = 5,
                Signal = "HOLD",
                Rsi = ind.RSI,
                Macd = ind.MACD.MACD,
                Confidence = 30,
                AnalysisSummary = response,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }
    }

    // ============ Helper Types ============

    private record TechnicalIndicators
    {
        public double CurrentPrice { get; init; }
        public double RSI { get; init; }
        public (double MACD, double Signal, double Histogram) MACD { get; init; }
        public double MA20 { get; init; }
        public double MA60 { get; init; }
        public double BollingerUpper { get; init; }
        public double BollingerLower { get; init; }
        public double ATR { get; init; }
        public double Support { get; init; }
        public double Resistance { get; init; }
    }
}
