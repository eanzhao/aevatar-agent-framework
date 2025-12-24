using Aevatar.Agents.AI.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.WithTool.Abstractions;

/// <summary>
/// 工具执行上下文
/// </summary>
public class ToolExecutionContext
{
    /// <summary>
    /// Agent ID
    /// </summary>
    public string AgentId { get; set; } = string.Empty;
    
    /// <summary>
    /// 工具管理器
    /// </summary>
    public IAevatarToolManager ToolManager { get; set; } = null!;
    
    /// <summary>
    /// 事件发布回调
    /// </summary>
    public Func<IMessage, Task>? PublishEventCallback { get; set; }
    
    /// <summary>
    /// 获取会话ID的函数
    /// </summary>
    public Func<string> GetSessionId { get; set; } = () => Guid.NewGuid().ToString();
    
    /// <summary>
    /// 日志记录器
    /// </summary>
    public ILogger? Logger { get; set; }
    
    /// <summary>
    /// 额外的上下文数据
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}