namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// Aevatar框架的大语言模型提供者接口
/// 提供与不同LLM后端（如OpenAI、Azure OpenAI、本地模型等）交互的统一接口
/// </summary>
public interface IAevatarLLMProvider
{
    /// <summary>
    /// 提供者唯一标识
    /// </summary>
    string ProviderId { get; }
    
    /// <summary>
    /// 提供者名称
    /// </summary>
    string ProviderName { get; }
    
    /// <summary>
    /// 生成文本响应
    /// </summary>
    Task<AevatarLLMResponse> GenerateAsync(
        AevatarLLMRequest request,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 流式生成文本响应
    /// </summary>
    IAsyncEnumerable<AevatarLLMToken> GenerateStreamAsync(
        AevatarLLMRequest request,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 生成文本嵌入向量
    /// </summary>
    Task<AevatarEmbeddingResponse> GenerateEmbeddingAsync(
        string text,
        AevatarEmbeddingOptions? options = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 批量生成文本嵌入向量
    /// </summary>
    Task<IReadOnlyList<AevatarEmbeddingResponse>> GenerateEmbeddingsAsync(
        IEnumerable<string> texts,
        AevatarEmbeddingOptions? options = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 检查提供者是否可用
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取模型信息
    /// </summary>
    Task<AevatarModelInfo> GetAevatarModelInfoAsync(string modelId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取可用模型列表
    /// </summary>
    Task<IReadOnlyList<AevatarModelInfo>> ListAvailableModelsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// LLM请求
/// </summary>
public class AevatarLLMRequest
{
    /// <summary>
    /// 系统提示词
    /// </summary>
    public string? SystemPrompt { get; set; }
    
    /// <summary>
    /// 用户提示词
    /// </summary>
    public string UserPrompt { get; set; } = string.Empty;
    
    /// <summary>
    /// 对话历史
    /// </summary>
    public IList<AevatarChatMessage> Messages { get; set; } = new List<AevatarChatMessage>();
    
    /// <summary>
    /// 模型设置
    /// </summary>
    public AevatarLLMSettings Settings { get; set; } = new();
    
    /// <summary>
    /// 函数/工具定义（用于Function Calling）
    /// </summary>
    public IList<AevatarFunctionDefinition>? Functions { get; set; }
    
    /// <summary>
    /// 上下文窗口中的额外信息
    /// </summary>
    public Dictionary<string, object>? Context { get; set; }
}

/// <summary>
/// LLM响应
/// </summary>
public class AevatarLLMResponse
{
    /// <summary>
    /// 生成的内容
    /// </summary>
    public string Content { get; set; } = string.Empty;
    
    /// <summary>
    /// 函数调用（如果有）
    /// </summary>
    public AevatarFunctionCall? AevatarFunctionCall { get; set; }
    
    /// <summary>
    /// 停止原因
    /// </summary>
    public AevatarStopReason AevatarStopReason { get; set; }
    
    /// <summary>
    /// Token使用情况
    /// </summary>
    public AevatarTokenUsage? Usage { get; set; }
    
    /// <summary>
    /// 模型名称
    /// </summary>
    public string? ModelName { get; set; }
    
    /// <summary>
    /// 元数据
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// 聊天消息
/// </summary>
public class AevatarChatMessage
{
    /// <summary>
    /// 角色（system/user/assistant/function）
    /// </summary>
    public AevatarChatRole Role { get; set; }
    
    /// <summary>
    /// 消息内容
    /// </summary>
    public string Content { get; set; } = string.Empty;
    
    /// <summary>
    /// 函数名称（当Role为Function时）
    /// </summary>
    public string? FunctionName { get; set; }
    
    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 聊天角色
/// </summary>
public enum AevatarChatRole
{
    System,
    User,
    Assistant,
    Function
}

/// <summary>
/// LLM设置
/// </summary>
public class AevatarLLMSettings
{
    /// <summary>
    /// 模型ID
    /// </summary>
    public string? ModelId { get; set; }
    
    /// <summary>
    /// 温度参数（0-2，控制输出的随机性）
    /// </summary>
    public double Temperature { get; set; } = 0.7;
    
    /// <summary>
    /// Top-P参数（核采样）
    /// </summary>
    public double TopP { get; set; } = 1.0;
    
    /// <summary>
    /// 最大生成Token数
    /// </summary>
    public int MaxTokens { get; set; } = 2000;
    
    /// <summary>
    /// 频率惩罚（-2.0到2.0）
    /// </summary>
    public double FrequencyPenalty { get; set; } = 0;
    
    /// <summary>
    /// 存在惩罚（-2.0到2.0）
    /// </summary>
    public double PresencePenalty { get; set; } = 0;
    
    /// <summary>
    /// 停止序列
    /// </summary>
    public IList<string>? StopSequences { get; set; }
    
    /// <summary>
    /// 响应格式（如json_object）
    /// </summary>
    public AevatarResponseFormat? AevatarResponseFormat { get; set; }
    
    /// <summary>
    /// Seed（用于确定性输出）
    /// </summary>
    public int? Seed { get; set; }
}

/// <summary>
/// 响应格式
/// </summary>
public class AevatarResponseFormat
{
    public string Type { get; set; } = "text";
    public object? Schema { get; set; }
}

/// <summary>
/// 流式Token
/// </summary>
public class AevatarLLMToken
{
    public string Content { get; set; } = string.Empty;
    public int Index { get; set; }
    public bool IsComplete { get; set; }
    public AevatarFunctionCall? AevatarFunctionCall { get; set; }
}

/// <summary>
/// Token使用情况
/// </summary>
public class AevatarTokenUsage
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
}

/// <summary>
/// 停止原因
/// </summary>
public enum AevatarStopReason
{
    Complete,
    MaxTokens,
    StopSequence,
    AevatarFunctionCall,
    ContentFilter,
    Error
}

/// <summary>
/// 函数定义（用于Function Calling）
/// </summary>
public class AevatarFunctionDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, AevatarParameterDefinition> Parameters { get; set; } = new();
    public bool Required { get; set; }
}

/// <summary>
/// 参数定义
/// </summary>
public class AevatarParameterDefinition
{
    public string Type { get; set; } = "string";
    public string? Description { get; set; }
    public bool Required { get; set; }
    public object? Default { get; set; }
    public IList<string>? Enum { get; set; }
}

/// <summary>
/// 函数调用
/// </summary>
public class AevatarFunctionCall
{
    public string Name { get; set; } = string.Empty;
    public string Arguments { get; set; } = "{}";
}

/// <summary>
/// 嵌入选项
/// </summary>
public class AevatarEmbeddingOptions
{
    public string? ModelId { get; set; }
    public int? Dimensions { get; set; }
}

/// <summary>
/// 嵌入响应
/// </summary>
public class AevatarEmbeddingResponse
{
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public int Index { get; set; }
    public string? ModelName { get; set; }
    public AevatarTokenUsage? Usage { get; set; }
}

/// <summary>
/// 模型信息
/// </summary>
public class AevatarModelInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public int MaxTokens { get; set; }
    public int ContextWindow { get; set; }
    public bool SupportsStreaming { get; set; }
    public bool SupportsFunctions { get; set; }
    public bool SupportsEmbeddings { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}
