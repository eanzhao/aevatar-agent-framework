using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Helpers;
using Aevatar.Agents.Local;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.Core.Tests.TestHelpers;

/// <summary>
/// 测试用的Agent工厂提供者
/// 为测试提供简单的工厂实现，避免需要复杂的DI配置
/// </summary>
public class TestGAgentActorFactoryProvider : IGAgentActorFactoryProvider
{
    private readonly IServiceProvider _serviceProvider;
    private readonly LocalMessageStreamRegistry _streamRegistry;
    
    public TestGAgentActorFactoryProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _streamRegistry = new LocalMessageStreamRegistry();
    }
    
    public void RegisterFactory<TAgent>(Func<IGAgentActorFactory, Guid, CancellationToken, Task<IGAgentActor>> factory) 
        where TAgent : IGAgent
    {
        // 测试中不需要注册，自动创建
    }
    
    public void RegisterFactory(Type agentType, Func<IGAgentActorFactory, Guid, CancellationToken, Task<IGAgentActor>> factory)
    {
        // 测试中不需要注册，自动创建
    }
    
    public Func<IGAgentActorFactory, Guid, CancellationToken, Task<IGAgentActor>>? GetFactory(Type agentType)
    {
        // 为所有测试Agent提供简单的工厂实现
        return async (factory, id, ct) =>
        {
            // 检查是否已存在
            if (_streamRegistry.StreamExists(id))
            {
                throw new InvalidOperationException($"Agent with id {id} already exists");
            }
            
            // 创建Agent实例 - 直接使用Activator
            var agent = Activator.CreateInstance(agentType, id) as IGAgent;
            if (agent == null)
            {
                throw new InvalidOperationException($"Failed to create agent instance of type {agentType.Name}");
            }
            
            // 自动注入Logger
            AgentLoggerInjector.InjectLogger(agent, _serviceProvider);
            
            // 创建LocalGAgentActor（用于测试）
            var actor = new LocalGAgentActor(
                agent,
                _streamRegistry,
                _serviceProvider.GetService<Microsoft.Extensions.Logging.ILogger<LocalGAgentActor>>()
            );
            
            // 自动注入Actor的Logger
            AgentLoggerInjector.InjectLogger(actor, _serviceProvider);
            
            // 激活
            await actor.ActivateAsync(ct);
            
            return actor;
        };
    }
}
