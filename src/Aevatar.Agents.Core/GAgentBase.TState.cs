using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Persistence;
using Aevatar.Agents.Core.StateProtection;
using System.Diagnostics;
using Google.Protobuf;

namespace Aevatar.Agents.Core;

/// <summary>
/// Stateful agent base class
/// Extends the non-generic GAgentBase with state management capabilities
/// </summary>
/// <typeparam name="TState">Agent state type</typeparam>
public abstract class GAgentBase<TState> : GAgentBase, IStateGAgent<TState>
    where TState : class, IMessage<TState>, new()
{
    // ============ Fields ============

    private TState _state = new();

    /// <summary>
    /// State object - should only be modified within event handlers.
    /// Direct State assignment is protected, but individual property modifications cannot be intercepted
    /// for Protobuf-generated classes. Follow best practices:
    /// - Only modify State within [EventHandler] methods
    /// - Use events to trigger state changes
    /// - Direct modifications outside event handlers break the Actor model consistency
    /// </summary>
    protected TState State
    {
        get
        {
            // For development/debug builds, we can add a warning when accessing State outside handlers
#if DEBUG
            if (!StateProtectionContext.IsModifiable)
            {
                var callerMethod = new StackFrame(1)?.GetMethod()?.Name ?? "Unknown";
                if (!IsAllowedStateAccessMethod(callerMethod))
                {
                    Debug.WriteLine(
                        $"WARNING: State accessed from '{callerMethod}' outside event handler context. " +
                        "State should only be modified within event handlers.");
                }
            }
#endif
            return _state;
        }
        set
        {
            StateProtectionContext.EnsureModifiable("Direct State assignment");
            _state = value;
        }
    }

    /// <summary>
    /// Validates if the current context allows State modification.
    /// Throws an exception if not in a valid context.
    /// </summary>
    protected void ValidateStateModificationContext(string operationName = "State modification")
    {
        StateProtectionContext.EnsureModifiable(operationName);
    }

#if DEBUG
    protected virtual bool IsAllowedStateAccessMethod(string methodName)
    {
        // Allow certain methods to access State without warning
        return methodName switch
        {
            nameof(GetState) => true,
            nameof(GetDescription) => true,
            nameof(GetDescriptionAsync) => true,
            nameof(OnActivateAsync) => true,
            nameof(ToString) => true,
            nameof(HandleEventAsync) => true,
            _ => false
        };
    }
#endif

    /// <summary>
    /// StateStore (injected by Actor layer)
    /// </summary>
    protected IStateStore<TState>? StateStore { get; set; }

    // ============ Constructors ============

    public GAgentBase()
    {
    }

    public GAgentBase(Guid id) : base(id)
    {
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        if (StateStore != null)
        {
            await StateStore.SaveAsync(Id, _state, ct);
        }
    }

    // ============ IStateGAgent Implementation ============

    public TState GetState()
    {
        return _state.Clone(); // Return clone of state for read-only access
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
            // Allow state loading without protection during event handling setup
            using (StateProtectionContext.BeginEventHandlerScope())
            {
                _state = await StateStore.LoadAsync(Id, ct) ?? new TState();
            }
        }

        // 2. Call core event handling implementation
        await HandleEventCoreAsync(envelope, ct);

        // 3. Save State (if StateStore is configured)
        if (StateStore != null)
        {
            await StateStore.SaveAsync(Id, _state, ct);
        }
    }
}