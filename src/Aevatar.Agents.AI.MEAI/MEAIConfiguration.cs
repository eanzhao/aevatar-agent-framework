using Microsoft.Extensions.AI;

namespace Aevatar.Agents.AI.MEAI;

/// <summary>
/// Configuration for Microsoft.Extensions.AI
/// </summary>
public class MEAIConfiguration
{
    /// <summary>
    /// ChatClient实例（直接提供时使用）
    /// </summary>
    public IChatClient? ChatClient { get; set; }

    /// <summary>
    /// Provider类型（azure, openai等）
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// 模型名称
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// API密钥
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Azure端点
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Azure部署名称
    /// </summary>
    public string? DeploymentName { get; set; }

    /// <summary>
    /// 是否使用Azure CLI认证
    /// </summary>
    public bool UseAzureCliAuth { get; set; }
}