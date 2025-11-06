namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// AI处理策略接口
/// 定义不同AI处理模式的通用契约
/// </summary>
// ReSharper disable once InconsistentNaming
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