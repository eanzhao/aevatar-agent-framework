using System.Collections.Generic;

namespace Aevatar.Agents.AI.Abstractions.Configuration;

/// <summary>
/// LLM提供商配置
/// <para/>
/// 在appsettings.json中配置多个LLM提供商
/// </summary>
public class LLMProviderConfig
{
    /// <summary>
    /// 提供商名称（唯一标识，如：openai-gpt4, azure-gpt35, local-llama等）
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 提供商类型（OpenAI, AzureOpenAI, Local, etc）
    /// </summary>
    public string ProviderType { get; set; } = string.Empty;

    /// <summary>
    /// API密钥
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// 模型名称（如：gpt-4, gpt-3.5-turbo, llama2-70b等）
    /// </summary>
    public string Model { get; set; } = "gpt-4";

    /// <summary>
    /// API终结点（可选，用于Azure或本地模型）
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// 部署名称（Azure OpenAI专用）
    /// </summary>
    public string? DeploymentName { get; set; }

    /// <summary>
    /// 温度参数（0-2）
    /// </summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>
    /// 最大令牌数
    /// </summary>
    public int MaxTokens { get; set; } = 2000;

    /// <summary>
    /// 超时时间（毫秒）
    /// </summary>
    public int TimeoutMilliseconds { get; set; } = 60000;

    /// <summary>
    /// 是否启用流式响应
    /// </summary>
    public bool EnableStreaming { get; set; } = false;

    /// <summary>
    /// 提供商特定设置
    /// </summary>
    public Dictionary<string, object> ProviderSpecificSettings { get; set; } = new();
}

/// <summary>
/// LLM提供商集合配置
/// <para/>
/// 在appsettings.json中的配置示例：
/// <code>
/// {
///   "LLMProviders": {
///     "default": "openai-gpt4",
///     "providers": {
///       "openai-gpt4": {
///         "providerType": "OpenAI",
///         "apiKey": "${OPENAI_API_KEY}",
///         "model": "gpt-4",
///         "temperature": 0.7
///       },
///       "azure-gpt35": {
///         "providerType": "AzureOpenAI",
///         "apiKey": "${AZURE_API_KEY}",
///         "endpoint": "https://your-resource.openai.azure.com",
///         "deploymentName": "gpt-35-turbo",
///         "model": "gpt-3.5-turbo"
///       },
///       "local-llama": {
///         "providerType": "Ollama",
///         "endpoint": "http://localhost:11434",
///         "model": "llama2:70b"
///       }
///     }
///   }
/// }
/// </code>
/// </summary>
public class LLMProvidersConfig
{
    /// <summary>
    /// 默认提供商名称
    /// </summary>
    public string Default { get; set; } = "openai-gpt4";

    /// <summary>
    /// 提供商字典（key: 提供商名称, value: 配置）
    /// </summary>
    public Dictionary<string, LLMProviderConfig> Providers { get; set; } = new();
}
