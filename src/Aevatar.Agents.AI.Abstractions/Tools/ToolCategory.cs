namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 工具分类
/// </summary>
public enum ToolCategory
{
    /// <summary>
    /// 核心工具（事件、状态管理等）
    /// </summary>
    Core,
    
    /// <summary>
    /// 记忆相关工具
    /// </summary>
    Memory,
    
    /// <summary>
    /// 通信工具
    /// </summary>
    Communication,
    
    /// <summary>
    /// 数据处理工具
    /// </summary>
    DataProcessing,
    
    /// <summary>
    /// 外部集成工具
    /// </summary>
    Integration,
    
    /// <summary>
    /// 自定义工具
    /// </summary>
    Custom
}