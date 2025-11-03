using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Microsoft.Extensions.Logging;

namespace Demo.Agents;

// WeatherAgentState 已在 demo_messages.proto 中定义

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
        // 更新状态
        State.Location = city;
        State.UpdateCount++;
        State.LastUpdate = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);

        // 模拟天气查询
        var weather = GenerateWeather(city);
        
        // 更新天气状态
        var parts = weather.Split(',');
        if (parts.Length >= 2)
        {
            State.Condition = parts[0].Trim();
            if (double.TryParse(parts[1].Replace("°C", "").Trim(), out var temp))
            {
                State.Temperature = temp;
            }
        }

        Console.WriteLine($"[WeatherAgent] 城市 {city} 天气查询: {weather}");

        return weather;
    }

    /// <summary>
    /// 获取查询统计
    /// </summary>
    public int GetQueryCount() => State.UpdateCount;

    private string GenerateWeather(string city)
    {
        var weathers = new[] { "晴天", "多云", "阴天", "小雨", "大雨", "雪" };
        var random = new Random(city.GetHashCode());
        var temp = random.Next(-10, 35);
        var weather = weathers[random.Next(weathers.Length)];
        return $"{weather}, {temp}°C";
    }
}
