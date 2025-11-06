namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 工具管理器接口 - 简化版
/// 提供Agent工具扩展的核心功能
/// </summary>
public interface IAevatarToolManager
{
    /// <summary>
    /// 注册工具（核心方法）
    /// </summary>
    Task RegisterToolAsync(
        ToolDefinition tool,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取所有可用工具（核心方法）
    /// </summary>
    Task<IReadOnlyList<ToolDefinition>> GetAvailableToolsAsync(
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 执行工具（核心方法）
    /// </summary>
    Task<ToolExecutionResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        ExecutionContext? context = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 生成函数定义，用于LLM的Function Calling（核心方法）
    /// </summary>
    Task<IReadOnlyList<AevatarFunctionDefinition>> GenerateFunctionDefinitionsAsync(
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取单个工具（可选实现）
    /// </summary>
    async Task<ToolDefinition?> GetToolAsync(
        string toolName,
        CancellationToken cancellationToken = default)
    {
        var tools = await GetAvailableToolsAsync(cancellationToken);
        return tools.FirstOrDefault(t => t.Name == toolName);
    }
}
