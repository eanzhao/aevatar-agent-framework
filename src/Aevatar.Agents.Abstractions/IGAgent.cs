namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Agent 基础接口
/// 所有 Agent 的最小抽象，只包含标识
/// </summary>
public interface IGAgent
{
    /// <summary>
    /// Agent 唯一标识符（Guid 类型，通用标识）
    /// </summary>
    Guid Id { get; }
}

/// <summary>
/// 有状态的 Agent 接口
/// </summary>
/// <typeparam name="TState">Agent 状态类型</typeparam>
public interface IGAgent<TState> : IGAgent 
    where TState : class, new()
{
    /// <summary>
    /// 获取当前状态（只读）
    /// </summary>
    TState GetState();
    
    /// <summary>
    /// 获取描述信息
    /// </summary>
    Task<string> GetDescriptionAsync();
}