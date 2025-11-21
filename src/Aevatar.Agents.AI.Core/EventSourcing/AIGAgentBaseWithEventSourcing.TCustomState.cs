using Aevatar.Agents.AI.Core;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.AI.Core.EventSourcing;

/// <summary>
/// AI Agent with Event Sourcing capabilities (Generic CustomState, Default Config).
/// Combines AI decision-making with event-sourced state management for Custom State.
/// </summary>
/// <typeparam name="TCustomState">The business state type (must be protobuf)</typeparam>
public abstract class AIGAgentBaseWithEventSourcing<TCustomState> : AIGAgentBaseWithEventSourcing
    where TCustomState : class, IMessage<TCustomState>, new()
{
    private readonly StatePropertyAccessor<TCustomState> _customStateAccessor;

    #region Custom State Property

    protected TCustomState CustomState
    {
        get => _customStateAccessor.GetValue(State.CustomState);
        set => State.CustomState = _customStateAccessor.SetValue(value, "Direct State assignment");
    }

    public TCustomState GetCustomState() => _customStateAccessor.GetValue(State.CustomState, false);

    #endregion

    #region Constructors

    public AIGAgentBaseWithEventSourcing()
    {
        _customStateAccessor = new StatePropertyAccessor<TCustomState>();
    }

    public AIGAgentBaseWithEventSourcing(Guid id) : base(id)
    {
        _customStateAccessor = new StatePropertyAccessor<TCustomState>();
    }

    #endregion

    #region State Transitions

    protected abstract void TransitionState(TCustomState state, IMessage evt);

    protected override void TransitionState(AevatarAIAgentState state, IMessage evt)
    {
        var customState = state.CustomState == null ? new TCustomState() : state.CustomState.Unpack<TCustomState>();
        TransitionState(customState, evt);
        state.CustomState = Any.Pack(customState);
    }

    #endregion
}