using Demo.Agents;
using Microsoft.AspNetCore.Mvc;

namespace Demo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WeatherController : ControllerBase
{
    private readonly IServiceProvider _serviceProvider;

    public WeatherController(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// 查询指定城市的天气
    /// </summary>
    [HttpGet("{city}")]
    public async Task<IActionResult> GetWeather(string city)
    {
        try
        {
            // 通过DI获取Agent实例
            var agent = _serviceProvider.GetRequiredService<WeatherAgent>();
            var weather = await agent.GetWeatherAsync(city);

            return Ok(new
            {
                City = city,
                Weather = weather,
                AgentId = agent.Id,
                QueryCount = agent.GetQueryCount()
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    /// <summary>
    /// 获取Agent统计信息
    /// </summary>
    [HttpGet("info")]
    public IActionResult GetInfo()
    {
        try
        {
            return Ok(new
            {
                Message = "Weather Agent API",
                Version = "1.0.0",
                Description = "查询城市天气信息"
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }
}
