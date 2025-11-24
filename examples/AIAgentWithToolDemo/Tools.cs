using Aevatar.Agents.AI.WithTool.Abstractions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace AIAgentWithToolDemo;

/// <summary>
/// 简单的计算器工具，用于演示 Tool 的使用
/// </summary>
public class CalculatorTool : AevatarToolBase
{
    public override string Name => "calculator";

    public override string Description => "执行数学计算，支持加减乘除";

    public override ToolCategory Category => ToolCategory.Custom;

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["operation"] = new ToolParameter
                {
                    Type = "string",
                    Description = "操作类型: add, subtract, multiply, divide",
                    Required = true,
                    Enum = new List<object> { "add", "subtract", "multiply", "divide" }
                },
                ["a"] = new ToolParameter
                {
                    Type = "number",
                    Description = "第一个数字",
                    Required = true
                },
                ["b"] = new ToolParameter
                {
                    Type = "number",
                    Description = "第二个数字",
                    Required = true
                }
            },
            Required = new[] { "operation", "a", "b" }
        };
    }

    public override async Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        var operation = parameters["operation"].ToString();
        double a, b;

        if (parameters.ContainsKey("numbers") && parameters["numbers"] is System.Text.Json.JsonElement numbersElement && numbersElement.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var numbers = System.Text.Json.JsonSerializer.Deserialize<double[]>(numbersElement.GetRawText());
            if (numbers != null && numbers.Length >= 2)
            {
                a = numbers[0];
                b = numbers[1];
            }
            else
            {
                throw new ArgumentException("numbers array must contain at least 2 numbers");
            }
        }
        else if (parameters.ContainsKey("numbers") && parameters["numbers"] is List<object> numbersList)
        {
             a = Convert.ToDouble(numbersList[0]);
             b = Convert.ToDouble(numbersList[1]);
        }
        else
        {
            a = Convert.ToDouble(parameters["a"]);
            b = Convert.ToDouble(parameters["b"]);
        }

        double result = operation switch
        {
            "add" => a + b,
            "subtract" => a - b,
            "multiply" => a * b,
            "divide" => b != 0 ? a / b : throw new InvalidOperationException("除数不能为0"),
            _ => throw new ArgumentException($"不支持的操作: {operation}")
        };

        logger?.LogInformation("🧮 计算器执行: {A} {Op} {B} = {Result}", 
            a, operation, b, result);

        await Task.CompletedTask;

        // 返回 Protobuf 消息
        return new Struct
        {
            Fields =
            {
                ["result"] = Value.ForNumber(result),
                ["expression"] = Value.ForString($"{a} {operation} {b} = {result}")
            }
        };
    }
}

/// <summary>
/// 天气查询工具（模拟）
/// </summary>
public class WeatherTool : AevatarToolBase
{
    public override string Name => "get_weather";

    public override string Description => "获取指定城市的天气信息";

    public override ToolCategory Category => ToolCategory.Custom;

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["city"] = new ToolParameter
                {
                    Type = "string",
                    Description = "城市名称",
                    Required = true
                }
            },
            Required = new[] { "city" }
        };
    }

    public override async Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        var city = parameters["city"].ToString();

        // 模拟天气数据
        var random = new Random();
        var temperature = random.Next(15, 30);
        var conditions = new[] { "晴", "多云", "阴", "小雨" };
        var condition = conditions[random.Next(conditions.Length)];

        logger?.LogInformation("🌤️ 天气查询: {City} - {Temp}°C, {Condition}", 
            city, temperature, condition);

        await Task.Delay(100, cancellationToken); // 模拟API调用延迟

        // 返回 Protobuf 消息
        return new Struct
        {
            Fields =
            {
                ["city"] = Value.ForString(city ?? ""),
                ["temperature"] = Value.ForNumber(temperature),
                ["condition"] = Value.ForString(condition),
                ["message"] = Value.ForString($"{city}的天气: {condition}，温度 {temperature}°C")
            }
        };
    }
}
