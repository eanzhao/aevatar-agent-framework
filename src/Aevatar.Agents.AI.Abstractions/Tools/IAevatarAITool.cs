// Aevatar.Agent.AI.Abstractions
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Aevatar.Agents.AI.Abstractions.Tools;

/// <summary>
/// 简化的AI工具接口，带有aevatar前缀避免命名冲突
/// </summary>
public interface IAevatarAITool
{
    /// <summary>
    /// 工具名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 工具描述
    /// </summary>
    string Description { get; }

    /// <summary>
    /// 执行工具
    /// </summary>
    /// <param name="context">工具上下文</param>
    /// <param name="parameters">参数字典</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果</returns>
    Task<AevatarAIToolResult> ExecuteAsync(
        AevatarAIToolContext context,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// AI工具管理器接口
/// </summary>
public interface IAevatarAIToolManager
{
    /// <summary>
    /// 注册工具
    /// </summary>
    Task RegisterAevatarAIToolAsync(IAevatarAITool tool);

    /// <summary>
    /// 使用委托注册工具
    /// </summary>
    Task RegisterAevatarAIToolAsync(
        string name, 
        string description, 
        Func<AevatarAIToolContext, Dictionary<string, object>, CancellationToken, Task<AevatarAIToolResult>> implementation);

    /// <summary>
    /// 检查工具是否存在
    /// </summary>
    bool AevatarAIToolExists(string name);

    /// <summary>
    /// 获取工具
    /// </summary>
    IAevatarAITool? GetAevatarAITool(string name);

    /// <summary>
    /// 获取所有工具
    /// </summary>
    IEnumerable<IAevatarAITool> GetAllAevatarAITools();

    /// <summary>
    /// 执行工具
    /// </summary>
    Task<AevatarAIToolResult> ExecuteAevatarAIToolAsync(
        string name,
        AevatarAIToolContext context,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Tool descriptor for AI agents
/// </summary>
public interface IAevatarAIToolDescriptor
{
    /// <summary>
    /// Tool name
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Tool description
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// Tool parameters schema
    /// </summary>
    Dictionary<string, object> Parameters { get; }
}