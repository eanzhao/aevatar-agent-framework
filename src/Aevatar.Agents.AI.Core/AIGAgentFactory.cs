using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Core.Helpers;
using Aevatar.Agents.Core.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

public class AIGAgentFactory : IGAgentFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AIGAgentFactory>? _logger;

    public AIGAgentFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = serviceProvider.GetService<ILogger<AIGAgentFactory>>();
    }

    public IGAgent CreateGAgent(Guid id, Type agentType, CancellationToken ct = default)
    {
        // 创建 Agent 实例，支持多种构造函数模式
        IGAgent agent;

        // 尝试找到合适的构造函数
        var constructors = agentType.GetConstructors();

        var ctorWithOptionalGuid = constructors.FirstOrDefault(c =>
        {
            var parameters = c.GetParameters();
            return parameters.Length == 1 &&
                   parameters[0].ParameterType == typeof(Guid?) &&
                   parameters[0].HasDefaultValue;
        });

        if (ctorWithOptionalGuid != null)
        {
            agent = (IGAgent)ctorWithOptionalGuid.Invoke([id]);
        }
        else if (constructors.Any(c =>
                 {
                     var parameters = c.GetParameters();
                     return parameters.Length == 1 && parameters[0].ParameterType == typeof(Guid);
                 }))
        {
            var ctorWithGuid = agentType.GetConstructor([typeof(Guid)]);
            agent = (IGAgent)ctorWithGuid!.Invoke([id]);
        }
        else
        {
            agent = (IGAgent)Activator.CreateInstance(agentType)!;
        }

        AgentLoggerInjector.InjectLogger(agent, _serviceProvider);
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        AgentConfigStoreInjector.InjectConfigStore(agent, _serviceProvider);
        if (AIAgentLLMProviderFactoryInjector.HasLLMProviderFactory(agent))
        {
            AIAgentLLMProviderFactoryInjector.InjectLLMProviderFactory(agent, _serviceProvider);
        }

        return agent;
    }

    public TAgent CreateGAgent<TAgent>(Guid id, CancellationToken ct = default) where TAgent : IGAgent
    {
        return (TAgent)CreateGAgent(id, typeof(TAgent), ct);
    }

    public TAgent CreateGAgent<TAgent>(CancellationToken ct = default) where TAgent : IGAgent
    {
        return CreateGAgent<TAgent>(Guid.NewGuid(), ct);
    }
}