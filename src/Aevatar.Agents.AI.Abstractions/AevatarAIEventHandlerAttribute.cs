namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// AI事件处理器特性
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class AevatarAIEventHandlerAttribute : Attribute
{
    public bool UseStreaming { get; set; }
    public string? PromptTemplate { get; set; }
    public string[]? RequiredTools { get; set; }
    public AevatarAIProcessingMode Mode { get; set; } = AevatarAIProcessingMode.Standard;
}