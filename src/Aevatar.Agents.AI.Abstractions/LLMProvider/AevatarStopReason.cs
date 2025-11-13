namespace Aevatar.Agents.AI.Abstractions;

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