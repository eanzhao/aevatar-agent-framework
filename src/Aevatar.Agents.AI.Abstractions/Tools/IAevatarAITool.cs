// Aevatar.Agent.AI.Abstractions
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
/// AI工具执行结果
/// </summary>
public class AevatarAIToolResult
{
    /// <summary>
    /// 执行是否成功
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// 返回数据
    /// </summary>
    public object? Data { get; init; }

    /// <summary>
    /// 错误信息（如果失败）
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 元数据
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();

    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static AevatarAIToolResult CreateSuccess(object? data = null) =>
        new AevatarAIToolResult { Success = true, Data = data };

    /// <summary>
    /// 创建失败结果
    /// </summary>
    public static AevatarAIToolResult CreateFailure(string errorMessage) =>
        new AevatarAIToolResult { Success = false, ErrorMessage = errorMessage };
}

/// <summary>
/// AI工具上下文，轻量级设计
/// </summary>
public class AevatarAIToolContext
{
    /// <summary>
    /// 代理ID
    /// </summary>
    public string AgentId { get; init; } = string.Empty;

    /// <summary>
    /// 服务提供者
    /// </summary>
    public IServiceProvider ServiceProvider { get; init; } = null!;

    /// <summary>
    /// 取消令牌
    /// </summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// 获取服务
    /// </summary>
    public T GetService<T>() where T : notnull => ServiceProvider.GetRequiredService<T>();

    /// <summary>
    /// 获取配置
    /// </summary>
    public T? GetConfiguration<T>(string? section = null) where T : class, new()
    {
        var config = ServiceProvider.GetService<IConfiguration>();
        if (config == null) return new T();

        return section != null
            ? config.GetSection(section).Get<T>() ?? new T()
            : config.Get<T>() ?? new T();
    }
}

/// <summary>
/// Aevatar AI工具管理器接口
/// </summary>
public interface IAevatarAIToolManager
{
    /// <summary>
    /// 注册AI工具
    /// </summary>
    /// <param name="tool">要注册的工具</param>
    void RegisterAevatarAITool(IAevatarAITool tool);

    /// <summary>
    /// 注册AI工具（委托方式）
    /// </summary>
    /// <param name="name">工具名称</param>
    /// <param name="description">工具描述</param>
    /// <param name="executeFunc">执行函数</param>
    void RegisterAevatarAITool(
        string name,
        string description,
        Func<AevatarAIToolContext, Dictionary<string, object>, CancellationToken, Task<AevatarAIToolResult>> executeFunc);

    /// <summary>
    /// 获取指定名称的工具
    /// </summary>
    /// <param name="name">工具名称</param>
    /// <returns>工具实例，如果不存在则返回null</returns>
    IAevatarAITool? GetAevatarAITool(string name);

    /// <summary>
    /// 获取所有已注册的工具
    /// </summary>
    /// <returns>工具列表</returns>
    List<IAevatarAITool> GetAllAevatarAITools();

    /// <summary>
    /// 检查工具是否存在
    /// </summary>
    /// <param name="name">工具名称</param>
    /// <returns>如果存在返回true，否则返回false</returns>
    bool AevatarAIToolExists(string name);

    /// <summary>
    /// 执行指定的AI工具
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="context">工具上下文</param>
    /// <param name="parameters">执行参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果</returns>
    Task<AevatarAIToolResult> ExecuteAevatarAIToolAsync(
        string toolName,
        AevatarAIToolContext context,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default);
}