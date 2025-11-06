namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// AI处理策略接口
/// 定义不同AI处理模式的通用契约
/// </summary>
public interface IAevatarAIProcessingStrategy
{
    /// <summary>
    /// 获取策略名称
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// 获取处理模式
    /// </summary>
    AevatarAIProcessingMode Mode { get; }
    
    /// <summary>
    /// 处理AI请求
    /// </summary>
    /// <param name="context">AI上下文</param>
    /// <param name="config">事件处理配置</param>
    /// <param name="dependencies">策略依赖项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>处理结果</returns>
    Task<string> ProcessAsync(
        AevatarAIContext context,
        AevatarAIEventHandlerAttribute? config,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken = default);
}

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
    public IAevatarMemory Memory { get; init; } = null!;
    
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
