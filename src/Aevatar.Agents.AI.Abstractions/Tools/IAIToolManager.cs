namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// AI工具管理器接口
/// 负责管理、注册和执行AI可用的工具/函数
/// </summary>
public interface IAevatarToolManager
{
    /// <summary>
    /// 注册工具
    /// </summary>
    Task RegisterToolAsync(
        ToolDefinition tool,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量注册工具
    /// </summary>
    Task RegisterToolsAsync(
        IEnumerable<ToolDefinition> tools,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 注销工具
    /// </summary>
    Task<bool> UnregisterToolAsync(
        string toolName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取工具
    /// </summary>
    Task<ToolDefinition?> GetToolAsync(
        string toolName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有可用工具
    /// </summary>
    Task<IReadOnlyList<ToolDefinition>> GetAvailableToolsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据标签获取工具
    /// </summary>
    Task<IReadOnlyList<ToolDefinition>> GetToolsByTagAsync(
        string tag,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行工具
    /// </summary>
    Task<ToolExecutionResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        ExecutionContext? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量执行工具（并行）
    /// </summary>
    Task<IReadOnlyList<ToolExecutionResult>> ExecuteToolsAsync(
        IEnumerable<ToolExecution> executions,
        ExecutionContext? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 验证工具参数
    /// </summary>
    Task<ValidationResult> ValidateParametersAsync(
        string toolName,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 生成工具描述（用于LLM）
    /// </summary>
    Task<string> GenerateToolDescriptionsAsync(
        DescriptionFormat format = DescriptionFormat.Json,
        IEnumerable<string>? toolNames = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 生成函数定义（用于Function Calling）
    /// </summary>
    Task<IReadOnlyList<AevatarFunctionDefinition>> GenerateFunctionDefinitionsAsync(
        IEnumerable<string>? toolNames = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查工具是否可用
    /// </summary>
    Task<bool> IsToolAvailableAsync(
        string toolName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取工具执行历史
    /// </summary>
    Task<IReadOnlyList<ToolExecutionHistory>> GetExecutionHistoryAsync(
        string? toolName = null,
        DateTimeOffset? startTime = null,
        DateTimeOffset? endTime = null,
        int maxRecords = 100,
        CancellationToken cancellationToken = default);
}