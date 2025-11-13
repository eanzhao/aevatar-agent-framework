using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Tools.CoreTools;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core.Tools;

/// <summary>
/// 核心工具注册表
/// 提供所有内置工具的访问入口
/// 
/// 使用示例：
/// - 获取工具实例：CoreToolsRegistry.EventPublisher
/// - 创建工具定义：CoreToolsRegistry.EventPublisher.CreateToolDefinition(context, logger)
/// - 获取所有工具：CoreToolsRegistry.GetAllTools()
/// </summary>
public static class CoreToolsRegistry
{
    // 工具实例（懒加载）
    private static readonly Lazy<EventPublisherTool> _eventPublisher = new(() => new EventPublisherTool());
    private static readonly Lazy<StateQueryTool> _stateQuery = new(() => new StateQueryTool());
    private static readonly Lazy<MemorySearchTool> _memorySearch = new(() => new MemorySearchTool());

    /// <summary>
    /// 事件发布工具
    /// </summary>
    public static IAevatarTool EventPublisher => _eventPublisher.Value;

    /// <summary>
    /// 状态查询工具
    /// </summary>
    public static IAevatarTool StateQuery => _stateQuery.Value;

    /// <summary>
    /// 记忆搜索工具
    /// </summary>
    public static IAevatarTool MemorySearch => _memorySearch.Value;

    /// <summary>
    /// 获取所有核心工具
    /// </summary>
    public static IEnumerable<IAevatarTool> GetAllTools()
    {
        yield return EventPublisher;
        yield return StateQuery;
        yield return MemorySearch;
    }

    /// <summary>
    /// 获取指定类别的工具
    /// </summary>
    public static IEnumerable<IAevatarTool> GetToolsByCategory(ToolCategory category)
    {
        return GetAllTools().Where(t => t.Category == category);
    }

    /// <summary>
    /// 根据名称获取工具
    /// </summary>
    public static IAevatarTool? GetToolByName(string name)
    {
        return GetAllTools().FirstOrDefault(t => t.Name == name);
    }

    /// <summary>
    /// 创建所有核心工具定义
    /// </summary>
    public static IEnumerable<ToolDefinition> CreateAllToolDefinitions(ToolContext context, ILogger? logger = null)
    {
        foreach (var tool in GetAllTools())
        {
            yield return tool.CreateToolDefinition(context, logger);
        }
    }

    /// <summary>
    /// 验证参数
    /// </summary>
    public static ToolParameterValidationResult ValidateParameters(string toolName,
        Dictionary<string, object> parameters)
    {
        var tool = GetToolByName(toolName);
        if (tool == null)
        {
            return new ToolParameterValidationResult
            {
                IsValid = false,
                Errors = new List<string> { $"Tool '{toolName}' not found" }
            };
        }

        return tool.ValidateParameters(parameters);
    }
}