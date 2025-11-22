using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.WithProcessStrategy.Messages;

namespace Aevatar.Agents.AI.WithProcessStrategy.Abstractions;

/// <summary>
/// AI处理策略工厂接口
/// </summary>
public interface IAevatarAIProcessingStrategyFactory
{
    /// <summary>
    /// 获取处理策略
    /// </summary>
    IAevatarAIProcessingStrategy GetStrategy(AevatarAIProcessingMode mode);
    
    /// <summary>
    /// 获取或创建处理策略
    /// </summary>
    IAevatarAIProcessingStrategy GetOrCreateStrategy(AevatarAIProcessingMode mode, bool useCache = true);
    
    /// <summary>
    /// 注册自定义策略类型
    /// </summary>
    void RegisterStrategyType(AevatarAIProcessingMode mode, Type strategyType);
    
    /// <summary>
    /// 注册自定义策略实例
    /// </summary>
    void RegisterStrategy(AevatarAIProcessingMode mode, IAevatarAIProcessingStrategy strategy);
    
    /// <summary>
    /// 获取所有可用的处理模式
    /// </summary>
    IEnumerable<AevatarAIProcessingMode> GetAvailableModes();
    
    /// <summary>
    /// 清除策略缓存
    /// </summary>
    void ClearCache();
}