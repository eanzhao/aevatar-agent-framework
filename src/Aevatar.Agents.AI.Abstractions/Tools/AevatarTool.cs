namespace Aevatar.Agents.AI.Abstractions;

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
    public ToolCategory Category { get; set; } = ToolCategory.Custom;
    
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
    
    /// <summary>
    /// 是否需要内部访问权限
    /// </summary>
    public bool RequiresInternalAccess { get; set; }
    
    /// <summary>
    /// 是否可以被覆盖
    /// </summary>
    public bool CanBeOverridden { get; set; } = true;
}