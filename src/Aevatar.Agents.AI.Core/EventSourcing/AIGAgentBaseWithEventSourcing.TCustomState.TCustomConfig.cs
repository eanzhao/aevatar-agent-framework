using Aevatar.Agents.AI.Core;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.AI.Core.EventSourcing;

/// <summary>
/// AI Agent with Event Sourcing capabilities (Generic).
/// Combines AI decision-making with event-sourced state management for Custom State and Config.
/// </summary>
/// <typeparam name="TCustomState">The business state type (must be protobuf)</typeparam>
/// <typeparam name="TConfig">The configuration type (must be protobuf)</typeparam>
public abstract class AIGAgentBaseWithEventSourcing<TCustomState, TConfig> : AIGAgentBaseWithEventSourcing<TCustomState>
    where TCustomState : class, IMessage<TCustomState>, new()
    where TConfig : class, IMessage<TConfig>, new()
{
    private readonly StatePropertyAccessor<TConfig> _stateConfigAccessor;

    #region Custom Config Properties

    protected TConfig CustomConfig
    {
        get => _stateConfigAccessor.GetValue(Config.CustomConfig);
        set => Config.CustomConfig = _stateConfigAccessor.SetValue(value, "Direct Config assignment");
    }

    public TConfig GetCustomConfig() => _stateConfigAccessor.GetValue(Config.CustomConfig, false);

    #endregion

    #region Constructors

    public AIGAgentBaseWithEventSourcing()
    {
        _stateConfigAccessor = new StatePropertyAccessor<TConfig>();
    }

    public AIGAgentBaseWithEventSourcing(Guid id) : base(id)
    {
        _stateConfigAccessor = new StatePropertyAccessor<TConfig>();
    }

    #endregion
}