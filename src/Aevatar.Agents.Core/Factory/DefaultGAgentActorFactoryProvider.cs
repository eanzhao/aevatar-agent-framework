using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core.Factory;

/// <summary>
/// 简单的自动发现工厂提供者
/// 自动为所有 Agent 创建工厂，无需手动注册
/// </summary>
public class DefaultGAgentActorFactoryProvider : IGAgentActorFactoryProvider
{
    private readonly ILogger<DefaultGAgentActorFactoryProvider>? _logger;

    private readonly ConcurrentDictionary<Type, Func<IGAgentActorFactory, Guid, CancellationToken, Task<IGAgentActor>>>
        _factories;

    public DefaultGAgentActorFactoryProvider(IServiceProvider serviceProvider)
    {
        _logger = serviceProvider.GetService<ILogger<DefaultGAgentActorFactoryProvider>>();
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
        return _factories.GetValueOrDefault(agentType);
    }
}