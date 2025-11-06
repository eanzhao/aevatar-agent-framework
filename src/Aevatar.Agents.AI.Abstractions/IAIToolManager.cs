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

/// <summary>
/// AI工具定义
/// </summary>
public class AevatarTool
{
    /// <summary>
    /// 工具名称（唯一标识）
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// 显示名称
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
    
    /// <summary>
    /// 工具描述
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// 参数定义
    /// </summary>
    public AevatarAevatarToolParameters Parameters { get; set; } = new();
    
    /// <summary>
    /// 返回值定义
    /// </summary>
    public AevatarReturnValueDefinition? ReturnValue { get; set; }
    
    /// <summary>
    /// 执行函数
    /// </summary>
    public Func<Dictionary<string, object>, AevatarExecutionContext?, CancellationToken, Task<object?>>? ExecuteAsync { get; set; }
    
    /// <summary>
    /// 标签
    /// </summary>
    public IList<string> Tags { get; set; } = new List<string>();
    
    /// <summary>
    /// 类别
    /// </summary>
    public string? Category { get; set; }
    
    /// <summary>
    /// 是否需要确认
    /// </summary>
    public bool RequiresConfirmation { get; set; }
    
    /// <summary>
    /// 是否是危险操作
    /// </summary>
    public bool IsDangerous { get; set; }
    
    /// <summary>
    /// 速率限制（每分钟最大调用次数）
    /// </summary>
    public int? RateLimit { get; set; }
    
    /// <summary>
    /// 超时时间
    /// </summary>
    public TimeSpan? Timeout { get; set; }
    
    /// <summary>
    /// 重试策略
    /// </summary>
    public AevatarRetryPolicy? AevatarRetryPolicy { get; set; }
    
    /// <summary>
    /// 元数据
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
    
    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;
    
    /// <summary>
    /// 版本
    /// </summary>
    public string Version { get; set; } = "1.0.0";
}

/// <summary>
/// 工具参数定义
/// </summary>
public class AevatarAevatarToolParameters
{
    /// <summary>
    /// 参数字典
    /// </summary>
    public Dictionary<string, AevatarToolParameter> Items { get; set; } = new();
    
    /// <summary>
    /// 必需参数列表
    /// </summary>
    public IList<string> Required { get; set; } = new List<string>();
    
    /// <summary>
    /// 索引器
    /// </summary>
    public AevatarToolParameter this[string name]
    {
        get => Items[name];
        set => Items[name] = value;
    }
}

/// <summary>
/// 工具参数
/// </summary>
public class AevatarToolParameter
{
    /// <summary>
    /// 参数类型
    /// </summary>
    public string Type { get; set; } = "string";
    
    /// <summary>
    /// 参数描述
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// 是否必需
    /// </summary>
    public bool Required { get; set; }
    
    /// <summary>
    /// 默认值
    /// </summary>
    public object? DefaultValue { get; set; }
    
    /// <summary>
    /// 枚举值
    /// </summary>
    public IList<object>? Enum { get; set; }
    
    /// <summary>
    /// 最小值（数字类型）
    /// </summary>
    public double? Minimum { get; set; }
    
    /// <summary>
    /// 最大值（数字类型）
    /// </summary>
    public double? Maximum { get; set; }
    
    /// <summary>
    /// 最小长度（字符串/数组）
    /// </summary>
    public int? MinLength { get; set; }
    
    /// <summary>
    /// 最大长度（字符串/数组）
    /// </summary>
    public int? MaxLength { get; set; }
    
    /// <summary>
    /// 正则表达式模式
    /// </summary>
    public string? Pattern { get; set; }
    
    /// <summary>
    /// 格式（如email、uri、date-time等）
    /// </summary>
    public string? Format { get; set; }
}

/// <summary>
/// 返回值定义
/// </summary>
public class AevatarReturnValueDefinition
{
    /// <summary>
    /// 返回值类型
    /// </summary>
    public string Type { get; set; } = "object";
    
    /// <summary>
    /// 返回值描述
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Schema定义（JSON Schema）
    /// </summary>
    public object? Schema { get; set; }
}

/// <summary>
/// 工具执行
/// </summary>
public class AevatarToolExecution
{
    /// <summary>
    /// 工具名称
    /// </summary>
    public string ToolName { get; set; } = string.Empty;
    
    /// <summary>
    /// 参数
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();
    
    /// <summary>
    /// 执行ID（用于追踪）
    /// </summary>
    public string? ExecutionId { get; set; }
}

/// <summary>
/// 执行上下文
/// </summary>
public class AevatarExecutionContext
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public string? UserId { get; set; }
    
    /// <summary>
    /// 会话ID
    /// </summary>
    public string? SessionId { get; set; }
    
    /// <summary>
    /// Agent ID
    /// </summary>
    public Guid? AgentId { get; set; }
    
    /// <summary>
    /// 追踪ID
    /// </summary>
    public string? TraceId { get; set; }
    
    /// <summary>
    /// 附加数据
    /// </summary>
    public Dictionary<string, object>? AdditionalData { get; set; }
}

/// <summary>
/// 工具执行结果
/// </summary>
public class AevatarAevatarToolExecutionResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// 结果数据
    /// </summary>
    public object? Result { get; set; }
    
    /// <summary>
    /// 错误信息
    /// </summary>
    public string? Error { get; set; }
    
    /// <summary>
    /// 执行时间
    /// </summary>
    public TimeSpan ExecutionTime { get; set; }
    
    /// <summary>
    /// 工具名称
    /// </summary>
    public string ToolName { get; set; } = string.Empty;
    
    /// <summary>
    /// 执行ID
    /// </summary>
    public string ExecutionId { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    
    /// <summary>
    /// 元数据
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// 验证结果
/// </summary>
public class AevatarValidationResult
{
    /// <summary>
    /// 是否有效
    /// </summary>
    public bool IsValid { get; set; }
    
    /// <summary>
    /// 错误列表
    /// </summary>
    public IList<AevatarValidationError> Errors { get; set; } = new List<AevatarValidationError>();
}

/// <summary>
/// 验证错误
/// </summary>
public class AevatarValidationError
{
    /// <summary>
    /// 参数名称
    /// </summary>
    public string? ParameterName { get; set; }
    
    /// <summary>
    /// 错误消息
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// 错误代码
    /// </summary>
    public string? ErrorCode { get; set; }
}

/// <summary>
/// 描述格式
/// </summary>
public enum AevatarDescriptionFormat
{
    /// <summary>
    /// JSON格式
    /// </summary>
    Json,
    
    /// <summary>
    /// YAML格式
    /// </summary>
    Yaml,
    
    /// <summary>
    /// Markdown格式
    /// </summary>
    Markdown,
    
    /// <summary>
    /// 纯文本格式
    /// </summary>
    PlainText
}

/// <summary>
/// 工具执行历史
/// </summary>
public class AevatarAevatarToolExecutionHistory
{
    /// <summary>
    /// 执行ID
    /// </summary>
    public string ExecutionId { get; set; } = string.Empty;
    
    /// <summary>
    /// 工具名称
    /// </summary>
    public string ToolName { get; set; } = string.Empty;
    
    /// <summary>
    /// 参数
    /// </summary>
    public Dictionary<string, object>? Parameters { get; set; }
    
    /// <summary>
    /// 结果
    /// </summary>
    public object? Result { get; set; }
    
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// 错误信息
    /// </summary>
    public string? Error { get; set; }
    
    /// <summary>
    /// 执行时间
    /// </summary>
    public TimeSpan ExecutionTime { get; set; }
    
    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }
    
    /// <summary>
    /// 执行上下文
    /// </summary>
    public AevatarExecutionContext? Context { get; set; }
}

/// <summary>
/// 重试策略
/// </summary>
public class AevatarRetryPolicy
{
    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetries { get; set; } = 3;
    
    /// <summary>
    /// 重试延迟
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    
    /// <summary>
    /// 是否使用指数退避
    /// </summary>
    public bool UseExponentialBackoff { get; set; }
    
    /// <summary>
    /// 最大延迟
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(1);
}
