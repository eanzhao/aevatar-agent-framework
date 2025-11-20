using Google.Protobuf;

namespace Aevatar.Agents.AI.Core;

// ReSharper disable InconsistentNaming
/// <summary>
/// Level 1: Basic AI Agent with chat capabilities using state-based conversation management.
/// Uses AIAgentConfig as the default configuration type.
/// </summary>
/// <typeparam name="TState">The business state type (defined by the developer using protobuf)</typeparam>
public abstract class AIGAgentBase<TState> : AIGAgentBase<TState, AevatarAIAgentConfig>
    where TState : class, IMessage<TState>, new()
{
    #region Constructors

    public AIGAgentBase()
    {
    }

    public AIGAgentBase(Guid id) : base(id)
    {
    }

    #endregion
}