using Microsoft.Extensions.AI;

namespace Aevatar.Agents.AI.MEAI;

/// <summary>
/// Microsoft.Extensions.AI Agent配置
/// </summary>
public class MEAIConfiguration
{
    /// <summary>
    /// 直接提供的ChatClient实例（优先使用）
    /// </summary>
    public IChatClient? ChatClient { get; set; }
    
    /// <summary>
    /// Provider类型（azure/openai）
    /// </summary>
    public string? Provider { get; set; }
    
    /// <summary>
    /// API端点（Azure）
    /// </summary>
    public string? Endpoint { get; set; }
    
    /// <summary>
    /// API密钥
    /// </summary>
    public string? ApiKey { get; set; }
    
    /// <summary>
    /// 模型名称
    /// </summary>
    public string? Model { get; set; } = "gpt-4";
    
    /// <summary>
    /// 部署名称（Azure）
    /// </summary>
    public string? DeploymentName { get; set; }
    
    /// <summary>
    /// 使用Azure CLI认证
    /// </summary>
    public bool UseAzureCliAuth { get; set; }
    
    /// <summary>
    /// Temperature参数（0.0-1.0）
    /// </summary>
    public double Temperature { get; set; } = 0.7;
    
    /// <summary>
    /// 最大Token数
    /// </summary>
    public int MaxTokens { get; set; } = 2000;
    
    /// <summary>
    /// Top-P参数
    /// </summary>
    public double TopP { get; set; } = 0.95;
    
    /// <summary>
    /// 频率惩罚
    /// </summary>
    public double FrequencyPenalty { get; set; } = 0.0;
    
    /// <summary>
    /// 存在惩罚
    /// </summary>
    public double PresencePenalty { get; set; } = 0.0;
}