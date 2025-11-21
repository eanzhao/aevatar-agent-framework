using Google.Protobuf;

namespace Aevatar.Agents.AI.Core;

// ReSharper disable InconsistentNaming
/// <summary>
/// Level 1: Basic AI Agent with chat capabilities using state-based conversation management.
/// Uses AIAgentConfig as the default configuration type.
/// </summary>
/// <typeparam name="TCustomState">The business state type (defined by the developer using protobuf)</typeparam>
public abstract class AIGAgentBase<TCustomState> : AIGAgentBase
    where TCustomState : class, IMessage<TCustomState>, new()
{
    private readonly StatePropertyAccessor<TCustomState> _customStateAccessor;

    protected TCustomState CustomState
    {
        get => _customStateAccessor.GetValue(State.CustomState);
        set => State.CustomState = _customStateAccessor.SetValue(value, "Direct State assignment");
    }

    public TCustomState GetCustomState() => _customStateAccessor.GetValue(State.CustomState, false);

    public AIGAgentBase()
    {
        _customStateAccessor = new StatePropertyAccessor<TCustomState>();
    }

    public AIGAgentBase(Guid id) : base(id)
    {
        _customStateAccessor = new StatePropertyAccessor<TCustomState>();
    }
}