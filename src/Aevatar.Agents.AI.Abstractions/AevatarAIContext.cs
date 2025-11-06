using Google.Protobuf;

namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// AI上下文
/// </summary>
public class AevatarAIContext
{
    public EventEnvelope? EventEnvelope { get; set; }
    public IMessage? AgentState { get; set; }
    public Dictionary<string, object> WorkingMemory { get; set; } = new();
    public IReadOnlyList<AevatarConversationMessage> RecentMessages { get; set; } = new List<AevatarConversationMessage>();
    public string? Question { get; set; }
}