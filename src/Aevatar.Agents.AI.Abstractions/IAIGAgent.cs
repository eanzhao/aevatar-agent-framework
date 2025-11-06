using Aevatar.Agents.AI.Abstractions;

namespace Aevatar.Agents.AI.Abstractions;

public interface IAIGAgent
{
    /// <summary>
    /// LLM提供者
    /// </summary>
    protected IAevatarLLMProvider LLMProvider => null!;
    
    /// <summary>
    /// 提示词管理器
    /// </summary>
    protected IAevatarPromptManager PromptManager => null!;
    
    /// <summary>
    /// 工具管理器
    /// </summary>
    protected IAevatarToolManager ToolManager => null!;
    
    /// <summary>
    /// 记忆管理器
    /// </summary>
    protected IAevatarMemory Memory => null!;
    
    /// <summary>
    /// AI配置
    /// </summary>
    protected AevatarAIAgentConfiguration Configuration => new();
}