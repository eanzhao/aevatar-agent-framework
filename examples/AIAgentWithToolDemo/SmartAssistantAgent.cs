using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.WithTool;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Microsoft.Extensions.Logging;

namespace AIAgentWithToolDemo;

/// <summary>
/// 带工具支持的智能助手
/// </summary>
public class SmartAssistantAgent : AIGAgentWithToolBase<AevatarAIAgentState>
{
    public SmartAssistantAgent() : base()
    {
        // Force tool manager initialization by accessing the property
        // This will trigger EnsureToolManagerInitialized() which calls RegisterTools()
        _ = ToolManager;
    }

    /// <summary>
    /// 注册工具
    /// </summary>
    protected override void RegisterTools()
    {
        Logger?.LogInformation("🔧 开始注册工具...");

        // 注册计算器工具
        var calculatorTool = new CalculatorTool();
        RegisterToolAsync(calculatorTool, Logger).Wait();
        Logger?.LogInformation("✅ 已注册工具: {Name} - {Description}", calculatorTool.Name, calculatorTool.Description);

        // 注册天气工具
        var weatherTool = new WeatherTool();
        RegisterToolAsync(weatherTool, Logger).Wait();
        Logger?.LogInformation("✅ 已注册工具: {Name} - {Description}", weatherTool.Name, weatherTool.Description);

        var registeredCount = GetRegisteredTools().Count;
        Logger?.LogInformation("🎉 工具注册完成！共 {Count} 个工具", registeredCount);
        
        if (registeredCount == 0)
        {
            Logger?.LogWarning("⚠️ 警告: GetRegisteredTools() 返回 0 个工具!");
        }
    }

    /// <summary>
    /// Get available tools for testing/debugging
    /// </summary>
    public IReadOnlyList<ToolDefinition> GetAvailableTools()
    {
        return GetRegisteredTools();
    }

    public override string SystemPrompt => @"
你是一个智能助手，具有工具调用能力。

你可以使用以下工具:
1. calculator - 执行数学计算 (加减乘除)
2. get_weather - 查询城市天气信息

当用户需要计算时，使用 calculator 工具。
当用户询问天气时，使用 get_weather 工具。

请用简洁、友好的方式回答用户的问题。
";
}
