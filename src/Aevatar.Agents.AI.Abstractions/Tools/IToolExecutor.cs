namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 工具执行器接口
/// 负责工具的执行、监控和事件发布
/// </summary>
public interface IToolExecutor
{
    /// <summary>
    /// 执行单个工具
    /// </summary>
    Task<ToolExecutionResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量执行工具
    /// </summary>
    Task<IEnumerable<ToolExecutionResult>> ExecuteToolsAsync(
        IEnumerable<ToolExecutionRequest> requests,
        ToolExecutionContext context,
        bool parallel = false,
        CancellationToken cancellationToken = default);
}