using Aevatar.Agents.AI.WithProcessStrategy.Messages;

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
    /// 获取策略描述
    /// 描述该策略的功能和用途
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// 获取处理模式
    /// </summary>
    AevatarAIProcessingMode Mode { get; }
    
    /// <summary>
    /// 判断该策略是否能够处理给定的AI上下文
    /// 允许策略根据上下文内容决定是否适合处理
    /// </summary>
    /// <param name="context">AI上下文</param>
    /// <returns>如果策略可以处理该上下文返回true，否则返回false</returns>
    bool CanHandle(AevatarAIContext context);
    
    /// <summary>
    /// 估算处理给定上下文的复杂度
    /// </summary>
    /// <param name="context">AI上下文</param>
    /// <returns>复杂度分数，从0（简单）到1（复杂）</returns>
    double EstimateComplexity(AevatarAIContext context);
    
    /// <summary>
    /// 验证策略是否具有所需的所有依赖项
    /// </summary>
    /// <param name="dependencies">策略依赖项</param>
    /// <returns>如果所有依赖项都满足返回true，否则返回false</returns>
    bool ValidateRequirements(AevatarAIStrategyDependencies dependencies);
    
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
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken = default);
}