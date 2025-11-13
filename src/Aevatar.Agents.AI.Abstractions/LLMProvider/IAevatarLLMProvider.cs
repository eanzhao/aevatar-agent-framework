namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// LLM提供者接口 - 简化版
/// 支持多框架实现（OpenAI、Azure、本地模型等）
/// </summary>
public interface IAevatarLLMProvider
{
    /// <summary>
    /// 生成文本响应（核心方法）
    /// </summary>
    Task<AevatarLLMResponse> GenerateAsync(
        AevatarLLMRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 流式生成（可选实现）
    /// </summary>
    IAsyncEnumerable<AevatarLLMToken> GenerateStreamAsync(
        AevatarLLMRequest request,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取模型信息（可选实现）
    /// </summary>
    Task<AevatarModelInfo> GetModelInfoAsync(CancellationToken cancellationToken = default)
    {
        // 默认实现
        return Task.FromResult(new AevatarModelInfo 
        { 
            Name = "unknown",
            MaxTokens = 4096,
            SupportsStreaming = false,
            SupportsFunctions = false
        });
    }
}
