using Aevatar.Agents.Maker.Messages;

namespace Aevatar.Agents.Maker.StateMachine;

// ============================================================
//  MAKER Execution State Machine (P3-4)
//  Explicit state transitions with validation
// ============================================================

/// <summary>
/// Manages state transitions for MAKER Coordinator Agent.
/// Ensures only valid state transitions occur.
/// </summary>
public static class MakerStateMachine
{
    // ============================================================
    //  Valid State Transitions
    // ============================================================
    
    private static readonly Dictionary<MakerExecutionState, HashSet<MakerExecutionState>> ValidTransitions = new()
    {
        [MakerExecutionState.MakerStateIdle] = 
        [
            MakerExecutionState.MakerStateInitializingWorkers
        ],
        
        [MakerExecutionState.MakerStateInitializingWorkers] = 
        [
            MakerExecutionState.MakerStateAssessingAtomicity,
            MakerExecutionState.MakerStateFailed,
            MakerExecutionState.MakerStateCancelled
        ],
        
        [MakerExecutionState.MakerStateAssessingAtomicity] = 
        [
            MakerExecutionState.MakerStateSolvingAtomic,     // Task is atomic -> solve directly
            MakerExecutionState.MakerStateDecomposing,       // Task is complex -> decompose
            MakerExecutionState.MakerStateFailed,
            MakerExecutionState.MakerStateCancelled
        ],
        
        [MakerExecutionState.MakerStateDecomposing] = 
        [
            MakerExecutionState.MakerStateVoting,            // Wait for decomposition proposals
            MakerExecutionState.MakerStateFailed,
            MakerExecutionState.MakerStateCancelled
        ],
        
        [MakerExecutionState.MakerStateSolvingAtomic] = 
        [
            MakerExecutionState.MakerStateVoting,            // Wait for solution proposals
            MakerExecutionState.MakerStateFailed,
            MakerExecutionState.MakerStateCancelled
        ],
        
        [MakerExecutionState.MakerStateVoting] = 
        [
            MakerExecutionState.MakerStateAssessingAtomicity, // Process subtasks after decomposition
            MakerExecutionState.MakerStateComposing,         // All subtasks done -> compose
            MakerExecutionState.MakerStateCompleted,         // Atomic task solved
            MakerExecutionState.MakerStateFailed,
            MakerExecutionState.MakerStateCancelled
        ],
        
        [MakerExecutionState.MakerStateComposing] = 
        [
            MakerExecutionState.MakerStateCompleted,
            MakerExecutionState.MakerStateFailed,
            MakerExecutionState.MakerStateCancelled
        ],
        
        // Terminal states - no outgoing transitions
        [MakerExecutionState.MakerStateCompleted] = [],
        [MakerExecutionState.MakerStateFailed] = [],
        [MakerExecutionState.MakerStateCancelled] = []
    };

    // ============================================================
    //  State Transition API
    // ============================================================
    
    /// <summary>
    /// Check if a state transition is valid.
    /// </summary>
    public static bool CanTransition(MakerExecutionState from, MakerExecutionState to)
    {
        return ValidTransitions.TryGetValue(from, out var valid) && valid.Contains(to);
    }

    /// <summary>
    /// Attempt to transition to a new state. Returns false if invalid.
    /// </summary>
    public static bool TryTransition(ref MakerExecutionState current, MakerExecutionState target)
    {
        if (!CanTransition(current, target))
            return false;
        
        current = target;
        return true;
    }

    /// <summary>
    /// Transition to a new state. Throws if invalid.
    /// </summary>
    public static void Transition(ref MakerExecutionState current, MakerExecutionState target)
    {
        if (!TryTransition(ref current, target))
        {
            throw new InvalidOperationException(
                $"Invalid state transition: {current} -> {target}. " +
                $"Valid targets: [{string.Join(", ", ValidTransitions.GetValueOrDefault(current) ?? [])}]");
        }
    }

    // ============================================================
    //  State Queries
    // ============================================================
    
    /// <summary>
    /// Check if current state is a terminal state (Completed, Failed, Cancelled).
    /// </summary>
    public static bool IsTerminal(MakerExecutionState state)
    {
        return state is MakerExecutionState.MakerStateCompleted 
            or MakerExecutionState.MakerStateFailed 
            or MakerExecutionState.MakerStateCancelled;
    }

    /// <summary>
    /// Check if execution is active (not idle and not terminal).
    /// </summary>
    public static bool IsActive(MakerExecutionState state)
    {
        return state != MakerExecutionState.MakerStateIdle && !IsTerminal(state);
    }

    /// <summary>
    /// Check if current state allows cancellation.
    /// </summary>
    public static bool CanCancel(MakerExecutionState state)
    {
        return IsActive(state);
    }

    /// <summary>
    /// Get human-readable state name.
    /// </summary>
    public static string GetStateName(MakerExecutionState state) => state switch
    {
        MakerExecutionState.MakerStateIdle => "Idle",
        MakerExecutionState.MakerStateInitializingWorkers => "Initializing Workers",
        MakerExecutionState.MakerStateAssessingAtomicity => "Assessing Atomicity",
        MakerExecutionState.MakerStateDecomposing => "Decomposing Task",
        MakerExecutionState.MakerStateSolvingAtomic => "Solving Atomic Task",
        MakerExecutionState.MakerStateVoting => "Voting",
        MakerExecutionState.MakerStateComposing => "Composing Results",
        MakerExecutionState.MakerStateCompleted => "Completed",
        MakerExecutionState.MakerStateFailed => "Failed",
        MakerExecutionState.MakerStateCancelled => "Cancelled",
        _ => "Unknown"
    };

    // ============================================================
    //  Legacy Status Mapping (backward compatibility)
    // ============================================================
    
    /// <summary>
    /// Convert legacy int status to MakerExecutionState.
    /// </summary>
    public static MakerExecutionState FromLegacyStatus(int status) => status switch
    {
        0 => MakerExecutionState.MakerStateIdle,
        1 => MakerExecutionState.MakerStateInitializingWorkers, // "starting"
        2 => MakerExecutionState.MakerStateVoting,              // "running" - approximate
        3 => MakerExecutionState.MakerStateCompleted,
        4 => MakerExecutionState.MakerStateFailed,
        5 => MakerExecutionState.MakerStateCancelled,
        _ => MakerExecutionState.MakerStateIdle
    };

    /// <summary>
    /// Convert MakerExecutionState to legacy int status.
    /// </summary>
    public static int ToLegacyStatus(MakerExecutionState state) => state switch
    {
        MakerExecutionState.MakerStateIdle => 0,
        MakerExecutionState.MakerStateInitializingWorkers => 1,
        MakerExecutionState.MakerStateAssessingAtomicity => 2,
        MakerExecutionState.MakerStateDecomposing => 2,
        MakerExecutionState.MakerStateSolvingAtomic => 2,
        MakerExecutionState.MakerStateVoting => 2,
        MakerExecutionState.MakerStateComposing => 2,
        MakerExecutionState.MakerStateCompleted => 3,
        MakerExecutionState.MakerStateFailed => 4,
        MakerExecutionState.MakerStateCancelled => 5,
        _ => 0
    };
}

// ============================================================
//  MAKER Worker State Machine
// ============================================================

/// <summary>
/// Manages state transitions for MAKER Worker Agent.
/// </summary>
public static class MakerWorkerStateMachine
{
    private static readonly Dictionary<MakerWorkerStatus, HashSet<MakerWorkerStatus>> ValidTransitions = new()
    {
        [MakerWorkerStatus.WorkerStatusIdle] = 
        [
            MakerWorkerStatus.WorkerStatusWorking
        ],
        
        [MakerWorkerStatus.WorkerStatusWorking] = 
        [
            MakerWorkerStatus.WorkerStatusIdle,      // Task completed, ready for next
            MakerWorkerStatus.WorkerStatusCompleted,  // All work done
            MakerWorkerStatus.WorkerStatusError
        ],
        
        [MakerWorkerStatus.WorkerStatusCompleted] = [],
        
        [MakerWorkerStatus.WorkerStatusError] = 
        [
            MakerWorkerStatus.WorkerStatusIdle       // Can recover from error
        ]
    };

    public static bool CanTransition(MakerWorkerStatus from, MakerWorkerStatus to)
    {
        return ValidTransitions.TryGetValue(from, out var valid) && valid.Contains(to);
    }

    public static bool TryTransition(ref MakerWorkerStatus current, MakerWorkerStatus target)
    {
        if (!CanTransition(current, target))
            return false;
        
        current = target;
        return true;
    }

    public static int ToLegacyStatus(MakerWorkerStatus status) => status switch
    {
        MakerWorkerStatus.WorkerStatusIdle => 0,
        MakerWorkerStatus.WorkerStatusWorking => 1,
        MakerWorkerStatus.WorkerStatusCompleted => 2,
        MakerWorkerStatus.WorkerStatusError => 0, // Error falls back to idle in legacy
        _ => 0
    };
}

