using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 工具上下文
/// 包含创建和执行工具所需的所有依赖
/// </summary>
public class ToolContext
{
    /// <summary>
    /// Agent ID
    /// </summary>
    public string AgentId { get; set; } = string.Empty;
    
    /// <summary>
    /// Agent 类型名称
    /// </summary>
    public string AgentType { get; set; } = string.Empty;
    
    /// <summary>
    /// 是否包含核心工具
    /// </summary>
    public bool IncludeCoreTools { get; set; } = true;
    
    /// <summary>
    /// 工具类别过滤器（如果为空则包含所有类别）
    /// </summary>
    public IList<ToolCategory>? Categories { get; set; }
    
    /// <summary>
    /// 获取Agent状态的回调
    /// </summary>
    public Func<IMessage>? GetStateCallback { get; set; }
    
    /// <summary>
    /// 发布事件的回调
    /// </summary>
    public Func<IMessage, Task>? PublishEventCallback { get; set; }
    
    /// <summary>
    /// 记忆管理器
    /// </summary>
    public IAevatarAIMemory? Memory { get; set; }
    
    /// <summary>
    /// 获取会话ID的回调
    /// </summary>
    public Func<string>? GetSessionIdCallback { get; set; }
    
    /// <summary>
    /// 日志记录器
    /// </summary>
    public ILogger? Logger { get; set; }
    
    /// <summary>
    /// 额外的配置数据
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}