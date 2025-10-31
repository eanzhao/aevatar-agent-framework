using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Microsoft.Extensions.Logging;

namespace Demo.Agents;

/// <summary>
/// 天气查询Agent状态
/// </summary>
public class WeatherAgentState
{
    public Dictionary<string, string> WeatherCache { get; set; } = new();
    public int QueryCount { get; set; }
}

/// <summary>
/// 示例：天气查询Agent
/// </summary>
public class WeatherAgent : GAgentBase<WeatherAgentState>
{
    public WeatherAgent(Guid id, ILogger<WeatherAgent>? logger = null)
        : base(id, logger)
    {
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Weather Agent - Provides weather information for cities");
    }

    /// <summary>
    /// 查询天气
    /// </summary>
    public async Task<string> GetWeatherAsync(string city, CancellationToken ct = default)
    {
        // 检查缓存
        if (_state.WeatherCache.TryGetValue(city, out var cached))
        {
            return cached;
        }

        // 模拟天气查询
        var weather = GenerateWeather(city);
        _state.WeatherCache[city] = weather;
        _state.QueryCount++;

        Console.WriteLine($"[WeatherAgent] 城市 {city} 天气查询: {weather}");

        return weather;
    }

    /// <summary>
    /// 获取查询统计
    /// </summary>
    public int GetQueryCount() => _state.QueryCount;

    private string GenerateWeather(string city)
    {
        var weathers = new[] { "晴天", "多云", "阴天", "小雨", "大雨", "雪" };
        var random = new Random(city.GetHashCode());
        var temp = random.Next(-10, 35);
        var weather = weathers[random.Next(weathers.Length)];
        return $"{weather}, {temp}°C";
    }
}
