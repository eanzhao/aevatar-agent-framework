namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Agent 管理器接口
/// 负责 Agent 类型发现、注册和元数据管理
/// </summary>
public interface IGAgentManager
{
    #region 类型发现

    /// <summary>
    /// 获取所有可用的 Agent 类型
    /// </summary>
    List<Type> GetAvailableAgentTypes();

    /// <summary>
    /// 获取所有可用的事件类型
    /// </summary>
    List<Type> GetAvailableEventTypes();

    /// <summary>
    /// 获取指定 Agent 类型支持的事件类型
    /// </summary>
    List<Type> GetSupportedEventTypes<TAgent>()
        where TAgent : IGAgent;

    /// <summary>
    /// 获取指定 Agent 类型支持的事件类型
    /// </summary>
    List<Type> GetSupportedEventTypes(Type agentType);

    /// <summary>
    /// 检查指定类型是否为有效的 Agent 类型
    /// </summary>
    bool IsValidAgentType(Type type);

    /// <summary>
    /// 检查指定类型是否为有效的事件类型
    /// </summary>
    bool IsValidEventType(Type type);

    #endregion

    #region 类型注册

    /// <summary>
    /// 注册 Agent 类型（用于插件系统）
    /// </summary>
    void RegisterAgentType(Type agentType);

    /// <summary>
    /// 注销 Agent 类型
    /// </summary>
    void UnregisterAgentType(Type agentType);

    /// <summary>
    /// 注册事件类型
    /// </summary>
    void RegisterEventType(Type eventType);

    /// <summary>
    /// 注销事件类型
    /// </summary>
    void UnregisterEventType(Type eventType);

    #endregion

    #region 元数据

    /// <summary>
    /// 获取 Agent 类型的元数据
    /// </summary>
    AgentTypeMetadata? GetAgentMetadata(Type agentType);

    /// <summary>
    /// 获取 Agent 类型的元数据
    /// </summary>
    AgentTypeMetadata? GetAgentMetadata<TAgent>()
        where TAgent : IGAgent;

    /// <summary>
    /// 获取所有 Agent 类型的元数据
    /// </summary>
    IReadOnlyList<AgentTypeMetadata> GetAllAgentMetadata();

    #endregion

    #region 插件支持

    /// <summary>
    /// 从程序集加载 Agent 类型
    /// </summary>
    /// <param name="assembly">要加载的程序集</param>
    /// <returns>加载的 Agent 类型数量</returns>
    int LoadAgentTypesFromAssembly(System.Reflection.Assembly assembly);

    /// <summary>
    /// 从程序集路径加载 Agent 类型
    /// </summary>
    /// <param name="assemblyPath">程序集文件路径</param>
    /// <returns>加载的 Agent 类型数量</returns>
    int LoadAgentTypesFromPath(string assemblyPath);

    /// <summary>
    /// 卸载来自指定程序集的所有 Agent 类型
    /// </summary>
    void UnloadAgentTypesFromAssembly(System.Reflection.Assembly assembly);

    #endregion
}

/// <summary>
/// Agent 类型元数据
/// </summary>
public record AgentTypeMetadata
{
    /// <summary>
    /// Agent 类型
    /// </summary>
    public Type AgentType { get; init; } = null!;

    /// <summary>
    /// Agent 类型名称
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Agent 描述
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 状态类型
    /// </summary>
    public Type? StateType { get; init; }

    /// <summary>
    /// 支持的事件类型
    /// </summary>
    public List<Type> SupportedEventTypes { get; init; } = new();

    /// <summary>
    /// 是否支持事件溯源
    /// </summary>
    public bool SupportsEventSourcing { get; init; }

    /// <summary>
    /// 是否支持配置
    /// </summary>
    public bool SupportsConfiguration { get; init; }

    /// <summary>
    /// 来源程序集
    /// </summary>
    public string? AssemblyName { get; init; }

    /// <summary>
    /// 注册时间
    /// </summary>
    public DateTimeOffset RegisteredAt { get; init; } = DateTimeOffset.UtcNow;
}




