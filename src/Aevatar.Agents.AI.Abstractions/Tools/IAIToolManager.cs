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
        AevatarTool tool,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量注册工具
    /// </summary>
    Task RegisterToolsAsync(
        IEnumerable<AevatarTool> tools,
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
    Task<AevatarTool?> GetToolAsync(
        string toolName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有可用工具
    /// </summary>
    Task<IReadOnlyList<AevatarTool>> GetAvailableToolsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据标签获取工具
    /// </summary>
    Task<IReadOnlyList<AevatarTool>> GetToolsByTagAsync(
        string tag,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行工具
    /// </summary>
    Task<AevatarAevatarToolExecutionResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        AevatarExecutionContext? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量执行工具（并行）
    /// </summary>
    Task<IReadOnlyList<AevatarAevatarToolExecutionResult>> ExecuteToolsAsync(
        IEnumerable<AevatarToolExecution> executions,
        AevatarExecutionContext? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 验证工具参数
    /// </summary>
    Task<AevatarValidationResult> ValidateParametersAsync(
        string toolName,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 生成工具描述（用于LLM）
    /// </summary>
    Task<string> GenerateToolDescriptionsAsync(
        AevatarDescriptionFormat format = AevatarDescriptionFormat.Json,
        IEnumerable<string>? toolNames = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 生成函数定义（用于Function Calling）
    /// </summary>
    Task<IReadOnlyList<AevatarFunctionDefinition>> GenerateAevatarFunctionDefinitionsAsync(
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
    Task<IReadOnlyList<AevatarAevatarToolExecutionHistory>> GetExecutionHistoryAsync(
        string? toolName = null,
        DateTimeOffset? startTime = null,
        DateTimeOffset? endTime = null,
        int maxRecords = 100,
        CancellationToken cancellationToken = default);
}