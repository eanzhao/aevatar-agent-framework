using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core.Factory;

/// <summary>
/// 简单的自动发现工厂提供者
/// 自动为所有 Agent 创建工厂，无需手动注册
/// </summary>
public class AutoDiscoveryGAgentActorFactoryProvider : IGAgentActorFactoryProvider
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AutoDiscoveryGAgentActorFactoryProvider>? _logger;
    private readonly IGAgentFactory? _agentFactory;

    private readonly ConcurrentDictionary<Type, Func<IGAgentActorFactory, Guid, CancellationToken, Task<IGAgentActor>>>
        _factories;

    public AutoDiscoveryGAgentActorFactoryProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = serviceProvider.GetService<ILogger<AutoDiscoveryGAgentActorFactoryProvider>>();
        _agentFactory = serviceProvider.GetService<IGAgentFactory>();
        _factories =
            new ConcurrentDictionary<Type, Func<IGAgentActorFactory, Guid, CancellationToken, Task<IGAgentActor>>>();
    }

    public void RegisterFactory<TAgent>(Func<IGAgentActorFactory, Guid, CancellationToken, Task<IGAgentActor>> factory)
        where TAgent : IGAgent
    {
        _factories[typeof(TAgent)] = factory;
    }

    public void RegisterFactory(Type agentType,
        Func<IGAgentActorFactory, Guid, CancellationToken, Task<IGAgentActor>> factory)
    {
        _factories[agentType] = factory;
    }

    public Func<IGAgentActorFactory, Guid, CancellationToken, Task<IGAgentActor>>? GetFactory(Type agentType)
    {
        // 如果已缓存，直接返回
        if (_factories.TryGetValue(agentType, out var cachedFactory))
        {
            return cachedFactory;
        }

        // 创建新的工厂
        var factory = CreateAutoFactory(agentType);
        if (factory != null)
        {
            _factories.TryAdd(agentType, factory);
        }

        return factory;
    }

    private Func<IGAgentActorFactory, Guid, CancellationToken, Task<IGAgentActor>>? CreateAutoFactory(Type agentType)
    {
        if (!typeof(IGAgent).IsAssignableFrom(agentType))
        {
            _logger?.LogWarning("Type {AgentType} does not implement IGAgent", agentType.Name);
            return null;
        }

        return async (factory, id, ct) =>
        {
            // 创建 Agent 实例
            IGAgent? agent;

            try
            {
                // 优先使用 IGAgentFactory 创建 Agent
                if (_agentFactory != null)
                {
                    _logger?.LogDebug("Creating agent using IGAgentFactory for type {AgentType}", agentType.Name);
                    agent = _agentFactory.CreateGAgent(id, agentType, ct);
                }
                else
                {
                    // 如果没有 IGAgentFactory，尝试旧的创建方式
                    _logger?.LogWarning(
                        "IGAgentFactory not found, falling back to direct creation for type {AgentType}",
                        agentType.Name);
                    throw new InvalidOperationException($"Failed to create agent instance of type {agentType.Name}");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to create agent instance of type {AgentType}", agentType.Name);
                throw new InvalidOperationException($"Failed to create agent instance of type {agentType.Name}", ex);
            }

            // 使用工厂的 CreateActorForAgentAsync 方法（如果存在）
            var factoryType = factory.GetType();
            var createActorMethod = factoryType.GetMethod("CreateActorForAgentAsync",
                [typeof(IGAgent), typeof(Guid), typeof(CancellationToken)]);

            if (createActorMethod != null)
            {
                _logger?.LogDebug("Using CreateActorForAgentAsync for type {AgentType}", agentType.Name);

                var task = createActorMethod.Invoke(factory, [agent, id, ct]) as Task<IGAgentActor>;
                if (task != null)
                {
                    return await task;
                }
            }

            // 如果工厂没有提供 CreateActorForAgentAsync，抛出异常
            throw new NotSupportedException(
                $"Factory {factoryType.Name} does not support automatic agent discovery. " +
                "Please implement CreateActorForAgentAsync method or register the agent manually.");
        };
    }
}