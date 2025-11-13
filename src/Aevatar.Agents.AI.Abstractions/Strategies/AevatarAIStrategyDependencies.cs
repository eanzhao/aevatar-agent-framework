namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// AI处理策略依赖项
/// 包含策略执行所需的外部依赖
/// </summary>
public class AevatarAIStrategyDependencies
{
    /// <summary>
    /// LLM提供者
    /// </summary>
    public IAevatarLLMProvider LLMProvider { get; init; } = null!;
    
    /// <summary>
    /// 提示词管理器
    /// </summary>
    public IAevatarPromptManager PromptManager { get; init; } = null!;
    
    /// <summary>
    /// 工具管理器
    /// </summary>
    public IAevatarToolManager ToolManager { get; init; } = null!;
    
    /// <summary>
    /// 记忆管理器
    /// </summary>
    public IAevatarAIMemory Memory { get; init; } = null!;
    
    /// <summary>
    /// AI配置
    /// </summary>
    public AevatarAIAgentConfiguration Configuration { get; init; } = null!;
    
    /// <summary>
    /// 日志记录器
    /// </summary>
    public Microsoft.Extensions.Logging.ILogger? Logger { get; init; }
    
    /// <summary>
    /// Agent ID
    /// </summary>
    public string AgentId { get; init; } = string.Empty;
    
    /// <summary>
    /// 事件发布回调
    /// </summary>
    public Func<Google.Protobuf.IMessage, Task>? PublishEventCallback { get; init; }
    
    /// <summary>
    /// 工具执行回调
    /// </summary>
    public Func<string, Dictionary<string, object>, CancellationToken, Task<object?>>? ExecuteToolCallback { get; init; }
}