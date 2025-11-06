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