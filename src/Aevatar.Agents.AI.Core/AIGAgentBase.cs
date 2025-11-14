using Google.Protobuf;

namespace Aevatar.Agents.AI.Core;

// ReSharper disable InconsistentNaming
/// <summary>
/// Level 1: Basic AI Agent with chat capabilities using state-based conversation management.
/// Uses AIAgentConfig as the default configuration type.
/// 第一级：使用基于状态的对话管理的具有基础聊天能力的AI代理（使用AIAgentConfig作为默认配置）
/// </summary>
/// <typeparam name="TState">The business state type (defined by the developer using protobuf)</typeparam>
public abstract class AIGAgentBase<TState> : AIGAgentBase<TState, AIAgentConfig>
    where TState : class, IMessage<TState>, new()
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the AIGAgentBase class.
    /// 初始化AIGAgentBase类的新实例
    /// </summary>
    protected AIGAgentBase() { }

    #endregion
}
