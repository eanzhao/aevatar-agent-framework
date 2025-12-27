using Aevatar.Trade.Infrastructure.WeexApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Trade;

/// <summary>
/// 服务注册扩展
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加 WEEX 交易系统服务
    /// </summary>
    public static IServiceCollection AddWeexTradingServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ============ Configuration ============
        
        services.Configure<WeexApiConfig>(configuration.GetSection("Weex"));
        services.Configure<TradingConfig>(configuration.GetSection("Trading"));
        services.Configure<AnalysisConfig>(configuration.GetSection("Analysis"));
        services.Configure<RiskControlConfig>(configuration.GetSection("RiskControl"));

        // ============ WEEX API ============
        
        services.AddHttpClient<IWeexApiClient, WeexApiClient>((sp, client) =>
        {
            var config = configuration.GetSection("Weex").Get<WeexApiConfig>();
            client.BaseAddress = new Uri(config?.BaseUrl ?? "https://api-spot.weex.com");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        services.AddSingleton<WeexWebSocketClient>();

        return services;
    }
}

// ============ Configuration Classes ============

/// <summary>
/// 交易配置
/// </summary>
public class TradingConfig
{
    public string Symbol { get; set; } = "BTCUSDT_SPBL";
    public string Interval { get; set; } = "15m";
    public double MaxPositionPct { get; set; } = 10;
    public double MaxTotalPositionPct { get; set; } = 30;
    public double MaxLossPerTrade { get; set; } = 2;
    public double MaxDailyLoss { get; set; } = 5;
    public int MinConfidenceToTrade { get; set; } = 60;
}

/// <summary>
/// 分析权重配置
/// </summary>
public class AnalysisConfig
{
    public double SentimentWeight { get; set; } = 0.3;
    public double TechnicalWeight { get; set; } = 0.4;
    public double NewsWeight { get; set; } = 0.3;
}

/// <summary>
/// 风控配置
/// </summary>
public class RiskControlConfig
{
    public int MaxConsecutiveLosses { get; set; } = 3;
    public int CooldownMinutes { get; set; } = 60;
    public double StopLossPct { get; set; } = 2;
    public double TakeProfitPct { get; set; } = 4;
}
