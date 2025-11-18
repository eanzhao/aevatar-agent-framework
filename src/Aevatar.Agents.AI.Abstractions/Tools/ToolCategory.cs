namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 工具分类
/// </summary>
public enum ToolCategory
{
    /// <summary>
    /// 核心系统工具（事件、状态、生命周期管理）
    /// 例如：PublishEvent, UpdateState, GetAgentInfo
    /// </summary>
    Core,
    
    /// <summary>
    /// 记忆管理工具
    /// 例如：StoreMemory, RetrieveMemory, SearchSemanticMemory
    /// </summary>
    Memory,
    
    /// <summary>
    /// 通信和消息传递工具
    /// 例如：SendMessage, BroadcastEvent, CallAPI
    /// </summary>
    Communication,
    
    /// <summary>
    /// 数据处理和转换工具
    /// 例如：ParseJSON, TransformData, AggregateResults
    /// </summary>
    DataProcessing,
    
    /// <summary>
    /// 外部系统集成工具
    /// 例如：ConnectDatabase, CallWebService, SyncData
    /// </summary>
    Integration,
    
    /// <summary>
    /// 信息获取和查询工具
    /// 例如：GetWeather, SearchWeb, QueryDatabase
    /// </summary>
    Information,
    
    /// <summary>
    /// 实用计算工具
    /// 例如：Calculate, ConvertUnits, FormatText
    /// </summary>
    Utility,
    
    /// <summary>
    /// 分析和洞察工具
    /// 例如：AnalyzeData, GenerateReport, PredictTrend
    /// </summary>
    Analytics,
    
    /// <summary>
    /// 安全和验证工具
    /// 例如：ValidateInput, CheckPermissions, EncryptData
    /// </summary>
    Security,
    
    /// <summary>
    /// 监控和可观测性工具
    /// 例如：LogEvent, TrackMetric, CreateAlert
    /// </summary>
    Monitoring,
    
    /// <summary>
    /// 工作流和编排工具
    /// 例如：StartWorkflow, WaitForCondition, Parallelize
    /// </summary>
    Orchestration,
    
    /// <summary>
    /// 自定义业务工具
    /// </summary>
    Custom
}