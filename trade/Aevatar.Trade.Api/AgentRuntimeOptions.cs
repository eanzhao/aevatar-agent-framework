namespace Aevatar.Trade.Api;

/// <summary>
/// Agent 运行时配置
/// </summary>
public class AgentRuntimeOptions
{
    public const string SectionName = "AgentRuntime";

    public AgentRuntimeType RuntimeType { get; set; } = AgentRuntimeType.Local;
    public OrleansOptions Orleans { get; set; } = new();
}

/// <summary>
/// 运行时类型
/// </summary>
public enum AgentRuntimeType
{
    Local,
    Orleans,
    ProtoActor
}

/// <summary>
/// Orleans 配置
/// </summary>
public class OrleansOptions
{
    public string ClusterId { get; set; } = "trade-cluster";
    public string ServiceId { get; set; } = "trade-service";
    public bool UseLocalhostClustering { get; set; } = true;
    public int SiloPort { get; set; } = 11111;
    public int GatewayPort { get; set; } = 30000;
}
