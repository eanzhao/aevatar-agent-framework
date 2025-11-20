using System.Threading;

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Aevatar.Agents.Core.Tests")]
[assembly: InternalsVisibleTo("Aevatar.Agents.AI.Core")]

namespace Aevatar.Agents.Core.StateProtection;

/// <summary>
/// Context tracker for State and Config protection mechanism.
/// Tracks whether current execution is within an event handler or initialization context.
/// 状态保护上下文跟踪器，用于确保State和Config只能在OnActivateAsync或事件处理器中被修改。
/// </summary>
internal static class StateProtectionContext
{
    /// <summary>
    /// AsyncLocal storage to track event handler execution context per async flow.
    /// This ensures thread-safe context tracking in async operations.
    /// </summary>
    private static readonly AsyncLocal<bool> _isInEventHandler = new();

    /// <summary>
    /// Gets whether current execution is within an event handler context.
    /// </summary>
    public static bool IsInEventHandler => _isInEventHandler.Value;

    /// <summary>
    /// Creates an event handler execution scope.
    /// State modifications are only allowed within this scope.
    /// </summary>
    public class EventHandlerScope : IDisposable
    {
        private readonly bool _previousValue;

        public EventHandlerScope()
        {
            _previousValue = _isInEventHandler.Value;
            _isInEventHandler.Value = true;
        }

        public void Dispose()
        {
            _isInEventHandler.Value = _previousValue;
        }
    }

    /// <summary>
    /// Begins an event handler execution scope.
    /// </summary>
    /// <returns>A disposable scope that must be disposed when event handling completes.</returns>
    public static EventHandlerScope BeginEventHandlerScope()
    {
        return new EventHandlerScope();
    }

    /// <summary>
    /// Checks if current context allows State modification.
    /// Throws InvalidOperationException if not in event handler context.
    /// </summary>
    /// <param name="operationName">Name of the operation attempting to modify state</param>
    /// <exception cref="InvalidOperationException">Thrown when not in event handler context</exception>
    public static void EnsureInEventHandler(string operationName = "State modification")
    {
        if (!IsInEventHandler)
        {
            throw new InvalidOperationException(
                $"{operationName} is not allowed outside of event handlers. " +
                "State must only be modified within event handler methods to ensure consistency through the event stream. " +
                "Consider publishing an event and handling it in an [EventHandler] method instead.");
        }
    }

    /// <summary>
    /// Temporarily allows State modification outside of event handler context.
    /// This should ONLY be used during agent initialization (OnActivateAsync).
    /// </summary>
    public class InitializationScope : IDisposable
    {
        private readonly bool _previousValue;

        public InitializationScope()
        {
            _previousValue = _isInEventHandler.Value;
            _isInEventHandler.Value = true;
        }

        public void Dispose()
        {
            _isInEventHandler.Value = _previousValue;
        }
    }

    /// <summary>
    /// Creates a special scope for agent initialization where State setup is allowed.
    /// Should only be used in OnActivateAsync or similar initialization methods.
    /// </summary>
    /// <returns>A disposable scope for initialization.</returns>
    public static InitializationScope BeginInitializationScope()
    {
        return new InitializationScope();
    }
}
