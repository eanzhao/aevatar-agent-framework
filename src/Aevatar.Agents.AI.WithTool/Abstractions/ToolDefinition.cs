using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.WithTool.Messages;
using Google.Protobuf;

namespace Aevatar.Agents.AI.WithTool.Abstractions;

/// <summary>
/// 工具定义
/// 描述一个可执行工具的运行时信息
/// </summary>
public class ToolDefinition
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
    public ToolParameters Parameters { get; set; } = new();
    
    /// <summary>
    /// 返回值定义
    /// </summary>
    public ToolReturnValue? ReturnValue { get; set; }
    
    /// <summary>
    /// 执行函数
    /// </summary>
    public Func<Dictionary<string, object>, ToolExecutionContext?, CancellationToken, Task<IMessage>>? ExecuteAsync { get; set; }
    
    /// <summary>
    /// 标签
    /// </summary>
    public IList<string> Tags { get; set; } = new List<string>();
    
    /// <summary>
    /// 类别
    /// </summary>
    public ToolCategory Category { get; set; } = ToolCategory.Custom;
    
    /// <summary>
    /// 版本
    /// </summary>
    public string Version { get; set; } = ToolConstants.DefaultVersion;
    
    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;
    
    /// <summary>
    /// 是否需要确认
    /// </summary>
    public bool RequiresConfirmation { get; set; }
    
    /// <summary>
    /// 是否是危险操作
    /// </summary>
    public bool IsDangerous { get; set; }
    
    /// <summary>
    /// 是否需要内部访问权限
    /// </summary>
    public bool RequiresInternalAccess { get; set; }
    
    /// <summary>
    /// 是否可以被覆盖
    /// </summary>
    public bool CanBeOverridden { get; set; } = true;
    
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
    public RetryPolicy? RetryPolicy { get; set; }
    
    /// <summary>
    /// 元数据
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// 工具参数定义
/// </summary>
public class ToolParameters
{
    /// <summary>
    /// 参数字典
    /// </summary>
    public Dictionary<string, ToolParameter> Items { get; set; } = new();
    
    /// <summary>
    /// 必需参数列表
    /// </summary>
    public IList<string> Required { get; set; } = new List<string>();
    
    /// <summary>
    /// 索引器
    /// </summary>
    public ToolParameter this[string name]
    {
        get => Items[name];
        set => Items[name] = value;
    }
}

/// <summary>
/// 工具参数
/// </summary>
public class ToolParameter
{
    /// <summary>
    /// 参数类型
    /// </summary>
    public string Type { get; set; } = ToolConstants.DefaultParameterType;
    
    /// <summary>
    /// 是否必需
    /// </summary>
    public bool Required { get; set; }
    
    /// <summary>
    /// 描述
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// 默认值
    /// </summary>
    public object? DefaultValue { get; set; }
    
    /// <summary>
    /// 枚举值（如果有限制）
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
    /// 最小长度（字符串类型）
    /// </summary>
    public int? MinLength { get; set; }
    
    /// <summary>
    /// 最大长度（字符串类型）
    /// </summary>
    public int? MaxLength { get; set; }
    
    /// <summary>
    /// 正则表达式模式（字符串类型）
    /// </summary>
    public string? Pattern { get; set; }
    
    /// <summary>
    /// 格式（如 email, uri, date-time 等）
    /// </summary>
    public string? Format { get; set; }
}

/// <summary>
/// 工具返回值定义
/// </summary>
public class ToolReturnValue
{
    /// <summary>
    /// 返回值类型
    /// </summary>
    public string Type { get; set; } = ToolConstants.DefaultReturnType;
    
    /// <summary>
    /// 描述
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// 架构定义（JSON Schema）
    /// </summary>
    public Dictionary<string, object>? Schema { get; set; }
}

/// <summary>
/// 重试策略
/// </summary>
public class RetryPolicy
{
    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetries { get; set; } = RetryPolicyDefaults.MaxRetries;
    
    /// <summary>
    /// 重试延迟（毫秒）
    /// </summary>
    public int RetryDelayMs { get; set; } = RetryPolicyDefaults.RetryDelayMs;
    
    /// <summary>
    /// 是否使用指数退避
    /// </summary>
    public bool UseExponentialBackoff { get; set; }
    
    /// <summary>
    /// 最大延迟（毫秒）
    /// </summary>
    public int MaxRetryDelayMs { get; set; } = RetryPolicyDefaults.MaxRetryDelayMs;
}