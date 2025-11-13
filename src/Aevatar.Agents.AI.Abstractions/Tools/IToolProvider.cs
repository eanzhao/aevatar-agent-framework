namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 工具提供者接口
/// 统一管理所有工具的创建和提供
/// </summary>
public interface IToolProvider
{
    /// <summary>
    /// 获取所有可用的工具定义
    /// </summary>
    /// <param name="context">工具上下文</param>
    /// <returns>工具定义列表</returns>
    Task<IEnumerable<ToolDefinition>> GetToolsAsync(ToolContext context);

    /// <summary>
    /// 获取特定的工具定义
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="context">工具上下文</param>
    /// <returns>工具定义，如果不存在则返回null</returns>
    Task<ToolDefinition?> GetToolAsync(string toolName, ToolContext context);

    /// <summary>
    /// 获取工具分类
    /// </summary>
    /// <returns>按类别分组的工具字典</returns>
    Task<IDictionary<ToolCategory, IList<string>>> GetToolCategoriesAsync();
}