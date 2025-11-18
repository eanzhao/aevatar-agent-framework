using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Persistence;
using Aevatar.Agents.Core.Observability;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Aevatar.Agents.Abstractions.Helpers;
using Google.Protobuf;

namespace Aevatar.Agents.Core;

/// <summary>
/// Stateful agent base class
/// Extends the non-generic GAgentBase with state management capabilities
/// </summary>
/// <typeparam name="TState">Agent state type</typeparam>
public abstract class GAgentBase<TState> : GAgentBase, IStateGAgent<TState>
    where TState : class, IMessage, new()
{
    // ============ Fields ============

    /// <summary>
    /// State object (writable, automatically persisted to StateStore)
    /// </summary>
    protected TState State { get; set; } = new();

    /// <summary>
    /// StateStore (injected by Actor layer)
    /// </summary>
    protected IStateStore<TState>? StateStore { get; set; }

    // ============ Constructors ============

    protected GAgentBase()
    {
    }

    protected GAgentBase(Guid id) : base(id)
    {
    }

    // ============ IStateGAgent Implementation ============

    public TState GetState()
    {
        return State;
    }

    // ============ Event Handling with State Persistence ============

    /// <summary>
    /// Handle event with automatic state loading and saving
    /// Extends the base implementation to add state persistence
    /// </summary>
    public override async Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        // 1. Load State (if StateStore is configured)
        if (StateStore != null)
        {
            State = await StateStore.LoadAsync(Id, ct) ?? new TState();
        }
        
        // 2. Call core event handling implementation
        await HandleEventCoreAsync(envelope, ct);
        
        // 3. Save State (if StateStore is configured)
        if (StateStore != null)
        {
            await StateStore.SaveAsync(Id, State, ct);
        }
    }
}