namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 提示词管理器接口
/// 负责管理、构建和优化提示词模板
/// </summary>
public interface IAevatarPromptManager
{
    /// <summary>
    /// 获取提示词模板
    /// </summary>
    Task<AevatarPromptTemplate?> GetTemplateAsync(
        string templateId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 注册提示词模板
    /// </summary>
    Task RegisterTemplateAsync(
        string templateId,
        AevatarPromptTemplate template,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新提示词模板
    /// </summary>
    Task UpdateTemplateAsync(
        string templateId,
        AevatarPromptTemplate template,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除提示词模板
    /// </summary>
    Task<bool> DeleteTemplateAsync(
        string templateId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 构建提示词
    /// </summary>
    Task<string> BuildPromptAsync(
        string templateId,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 构建对话提示词
    /// </summary>
    Task<IList<AevatarChatMessage>> BuildChatPromptAsync(
        string templateId,
        Dictionary<string, object> parameters,
        IList<AevatarChatMessage>? history = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 构建链式思考提示词
    /// </summary>
    Task<string> BuildChainOfThoughtPromptAsync(
        IEnumerable<AevatarThoughtStep> thoughts,
        string? finalQuestion = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 构建少样本学习提示词
    /// </summary>
    Task<string> BuildFewShotPromptAsync(
        string task,
        IEnumerable<AevatarExample> examples,
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 优化提示词（使用AI优化）
    /// </summary>
    Task<string> OptimizePromptAsync(
        string prompt,
        AevatarOptimizationGoal goal,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 验证提示词
    /// </summary>
    Task<AevatarPromptValidationResult> ValidatePromptAsync(
        string prompt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有模板ID
    /// </summary>
    Task<IReadOnlyList<string>> ListTemplateIdsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 导入模板集合
    /// </summary>
    Task ImportTemplatesAsync(
        string jsonContent,
        bool overwrite = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 导出模板集合
    /// </summary>
    Task<string> ExportTemplatesAsync(
        IEnumerable<string>? templateIds = null,
        CancellationToken cancellationToken = default);
}