namespace Aevatar.Agents.Orleans;

/// <summary>
/// Orleans Agent Actor 工厂配置选项
/// </summary>
public class OrleansGAgentActorFactoryOptions
{
    /// <summary>
    /// 是否使用事件溯源
    /// </summary>
    public bool UseEventSourcing { get; set; } = false;
    
    /// <summary>
    /// 是否使用 JournaledGrain（仅当 UseEventSourcing 为 true 时有效）
    /// </summary>
    public bool UseJournaledGrain { get; set; } = false;
    
    /// <summary>
    /// 默认 Grain 类型
    /// </summary>
    public GrainType DefaultGrainType { get; set; } = GrainType.Standard;
}

/// <summary>
/// Grain 类型枚举
/// </summary>
public enum GrainType
{
    /// <summary>
    /// 标准 Grain (OrleansGAgentGrain)
    /// </summary>
    Standard,
    
    /// <summary>
    /// 事件溯源 Grain (GenericEventSourcingGrain)
    /// </summary>
    EventSourcing,
    
    /// <summary>
    /// Journaled Grain (OrleansJournaledGAgentGrain)
    /// </summary>
    Journaled
}
