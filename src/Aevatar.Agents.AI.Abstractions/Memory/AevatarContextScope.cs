namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 上下文范围
/// </summary>
public enum AevatarContextScope
{
    /// <summary>
    /// 会话级别
    /// </summary>
    Session,
    
    /// <summary>
    /// Agent级别
    /// </summary>
    Agent,
    
    /// <summary>
    /// 全局级别
    /// </summary>
    Global,
    
    /// <summary>
    /// 临时（当前请求）
    /// </summary>
    Temporary
}