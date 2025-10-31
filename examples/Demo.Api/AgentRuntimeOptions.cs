namespace Demo.Api;

/// <summary>
/// Agent运行时类型
/// </summary>
public enum AgentRuntimeType
{
    /// <summary>
    /// 本地运行时（单机内存）
    /// </summary>
    Local,

    /// <summary>
    /// Orleans运行时（分布式）
    /// </summary>
    Orleans,

    /// <summary>
    /// Proto.Actor运行时
    /// </summary>
    ProtoActor
}

/// <summary>
/// Agent运行时配置选项
/// </summary>
public class AgentRuntimeOptions
{
    public const string SectionName = "AgentRuntime";

    /// <summary>
    /// 运行时类型
    /// </summary>
    public AgentRuntimeType RuntimeType { get; set; } = AgentRuntimeType.ProtoActor;

    /// <summary>
    /// Orleans配置（仅当RuntimeType为Orleans时使用）
    /// </summary>
    public OrleansOptions Orleans { get; set; } = new();
}

/// <summary>
/// Orleans运行时配置
/// </summary>
public class OrleansOptions
{
    /// <summary>
    /// 集群ID
    /// </summary>
    public string ClusterId { get; set; } = "dev";

    /// <summary>
    /// 服务ID
    /// </summary>
    public string ServiceId { get; set; } = "AgentService";

    /// <summary>
    /// Silo端口
    /// </summary>
    public int SiloPort { get; set; } = 11111;

    /// <summary>
    /// Gateway端口
    /// </summary>
    public int GatewayPort { get; set; } = 30000;

    /// <summary>
    /// 是否启用本地集群模式
    /// </summary>
    public bool UseLocalhostClustering { get; set; } = true;
}

