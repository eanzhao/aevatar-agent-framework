using System;
using System.Linq;
using System.Reflection;
using Aevatar.Agents.Abstractions;

namespace Aevatar.Agents.Core.Helpers;

/// <summary>
/// Agent 类型辅助工具
/// </summary>
public static class AgentTypeHelper
{
    /// <summary>
    /// 从 Agent 类型提取状态类型
    /// </summary>
    /// <param name="agentType">Agent 类型</param>
    /// <returns>状态类型</returns>
    public static Type ExtractStateType(Type agentType)
    {
        // 查找实现的 IGAgent<TState> 接口
        var gAgentInterface = agentType
            .GetInterfaces()
            .FirstOrDefault(i => 
                i.IsGenericType && 
                i.GetGenericTypeDefinition() == typeof(IStateGAgent<>));
        
        if (gAgentInterface == null)
        {
            // 检查基类
            var baseType = agentType.BaseType;
            while (baseType != null)
            {
                gAgentInterface = baseType
                    .GetInterfaces()
                    .FirstOrDefault(i => 
                        i.IsGenericType && 
                        i.GetGenericTypeDefinition() == typeof(IStateGAgent<>));
                
                if (gAgentInterface != null)
                {
                    break;
                }
                
                baseType = baseType.BaseType;
            }
        }
        
        if (gAgentInterface == null)
        {
            throw new InvalidOperationException(
                $"Type {agentType.Name} does not implement IGAgent<TState> interface");
        }
        
        // 提取泛型参数（即 TState）
        return gAgentInterface.GetGenericArguments()[0];
    }
    
    /// <summary>
    /// 通过反射调用双参数版本的 CreateAgentAsync
    /// </summary>
    public static async Task<IGAgentActor> InvokeCreateAgentAsync(
        IGAgentActorFactory factory,
        Type agentType,
        Type stateType,
        Guid id,
        CancellationToken ct = default)
    {
        // 获取 CreateAgentAsync<TAgent, TState> 方法
        var method = typeof(IGAgentActorFactory)
            .GetMethods()
            .First(m => 
                m.Name == "CreateAgentAsync" && 
                m.GetGenericArguments().Length == 2);
        
        // 构造泛型方法
        var genericMethod = method.MakeGenericMethod(agentType, stateType);
        
        // 调用方法
        var task = (Task<IGAgentActor>)genericMethod.Invoke(factory, new object[] { id, ct })!;
        return await task;
    }
}
