using Aevatar.Agents.AI.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Aevatar.Agents.AI.WithProcessStrategy.Abstractions;
using Aevatar.Agents.AI.WithProcessStrategy.Messages;

namespace Aevatar.Agents.AI.WithProcessStrategy.Strategies;

/// <summary>
/// AI处理策略工厂
/// 负责创建和管理不同的处理策略实例
/// </summary>
public class AevatarAIProcessingStrategyFactory : IAevatarAIProcessingStrategyFactory
{
    private readonly IServiceProvider? _serviceProvider;
    private readonly ILogger<AevatarAIProcessingStrategyFactory>? _logger;
    private readonly ConcurrentDictionary<AevatarAIProcessingMode, IAevatarAIProcessingStrategy> _strategies;
    private readonly Dictionary<AevatarAIProcessingMode, Type> _strategyTypes;
    
    /// <summary>
    /// 构造函数
    /// </summary>
    public AevatarAIProcessingStrategyFactory(
        IServiceProvider? serviceProvider = null,
        ILogger<AevatarAIProcessingStrategyFactory>? logger = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _strategies = new ConcurrentDictionary<AevatarAIProcessingMode, IAevatarAIProcessingStrategy>();
        
        // 注册内置策略类型
        _strategyTypes = new Dictionary<AevatarAIProcessingMode, Type>
        {
            [AevatarAIProcessingMode.Standard] = typeof(StandardProcessingStrategy),
            [AevatarAIProcessingMode.ChainOfThought] = typeof(ChainOfThoughtProcessingStrategy),
            [AevatarAIProcessingMode.ReAct] = typeof(ReActProcessingStrategy),
            [AevatarAIProcessingMode.TreeOfThoughts] = typeof(TreeOfThoughtsProcessingStrategy)
        };
        
        _logger?.LogDebug("AI Processing Strategy Factory initialized with {Count} strategy types", _strategyTypes.Count);
    }
    
    /// <summary>
    /// 获取处理策略
    /// </summary>
    public IAevatarAIProcessingStrategy GetStrategy(AevatarAIProcessingMode mode)
    {
        // 尝试从缓存获取
        if (_strategies.TryGetValue(mode, out var cachedStrategy))
        {
            _logger?.LogDebug("Using cached strategy for mode {Mode}", mode);
            return cachedStrategy;
        }
        
        // 创建新策略
        var strategy = CreateStrategy(mode);
        
        // 缓存策略（策略是无状态的，可以重用）
        _strategies.TryAdd(mode, strategy);
        
        return strategy;
    }
    
    /// <summary>
    /// 获取或创建处理策略（带依赖注入）
    /// </summary>
    public IAevatarAIProcessingStrategy GetOrCreateStrategy(
        AevatarAIProcessingMode mode,
        bool useCache = true)
    {
        if (useCache && _strategies.TryGetValue(mode, out var cachedStrategy))
        {
            return cachedStrategy;
        }
        
        var strategy = CreateStrategy(mode);
        
        if (useCache)
        {
            _strategies.TryAdd(mode, strategy);
        }
        
        return strategy;
    }
    
    /// <summary>
    /// 注册自定义策略类型
    /// </summary>
    public void RegisterStrategyType(AevatarAIProcessingMode mode, Type strategyType)
    {
        if (!typeof(IAevatarAIProcessingStrategy).IsAssignableFrom(strategyType))
        {
            throw new ArgumentException(
                $"Type {strategyType.Name} must implement IAevatarAIProcessingStrategy",
                nameof(strategyType));
        }
        
        _strategyTypes[mode] = strategyType;
        
        // 清除缓存的策略实例
        _strategies.TryRemove(mode, out _);
        
        _logger?.LogInformation("Registered custom strategy type {Type} for mode {Mode}", 
            strategyType.Name, mode);
    }
    
    /// <summary>
    /// 注册自定义策略实例
    /// </summary>
    public void RegisterStrategy(AevatarAIProcessingMode mode, IAevatarAIProcessingStrategy strategy)
    {
        _strategies[mode] = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _logger?.LogInformation("Registered custom strategy instance for mode {Mode}", mode);
    }
    
    /// <summary>
    /// 获取所有可用的处理模式
    /// </summary>
    public IEnumerable<AevatarAIProcessingMode> GetAvailableModes()
    {
        return _strategyTypes.Keys;
    }
    
    /// <summary>
    /// 清除策略缓存
    /// </summary>
    public void ClearCache()
    {
        _strategies.Clear();
        _logger?.LogDebug("Strategy cache cleared");
    }
    
    /// <summary>
    /// 创建策略实例
    /// </summary>
    private IAevatarAIProcessingStrategy CreateStrategy(AevatarAIProcessingMode mode)
    {
        if (!_strategyTypes.TryGetValue(mode, out var strategyType))
        {
            var message = $"No strategy registered for mode {mode}. Available modes: {string.Join(", ", _strategyTypes.Keys)}";
            _logger?.LogError(message);
            throw new NotSupportedException(message);
        }
        
        _logger?.LogDebug("Creating strategy instance for mode {Mode} using type {Type}", 
            mode, strategyType.Name);
        
        // 优先使用DI容器创建
        if (_serviceProvider != null)
        {
            try
            {
                var strategy = _serviceProvider.GetService(strategyType) as IAevatarAIProcessingStrategy;
                if (strategy != null)
                {
                    _logger?.LogDebug("Created strategy using DI container");
                    return strategy;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to create strategy using DI container, falling back to Activator");
            }
        }
        
        // 回退到Activator创建
        try
        {
            var strategy = Activator.CreateInstance(strategyType) as IAevatarAIProcessingStrategy;
            if (strategy != null)
            {
                _logger?.LogDebug("Created strategy using Activator");
                return strategy;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to create strategy instance for type {Type}", strategyType.Name);
            throw;
        }
        
        throw new InvalidOperationException($"Failed to create strategy instance for mode {mode}");
    }
}

/// <summary>
/// DI扩展方法
/// </summary>
public static class AevatarAIProcessingStrategyExtensions
{
    /// <summary>
    /// 注册AI处理策略服务
    /// </summary>
    public static IServiceCollection AddAevatarAIProcessingStrategies(this IServiceCollection services)
    {
        // 注册工厂
        services.AddSingleton<IAevatarAIProcessingStrategyFactory, AevatarAIProcessingStrategyFactory>();

        // 注册各个策略为瞬态服务（它们是无状态的）
        services.AddTransient<StandardProcessingStrategy>();
        services.AddTransient<ChainOfThoughtProcessingStrategy>();
        services.AddTransient<ReActProcessingStrategy>();
        services.AddTransient<TreeOfThoughtsProcessingStrategy>();

        // 注册泛型策略解析
        services.AddTransient<IAevatarAIProcessingStrategy>(provider =>
        {
            // 默认返回标准策略
            return provider.GetRequiredService<StandardProcessingStrategy>();
        });

        return services;
    }
}