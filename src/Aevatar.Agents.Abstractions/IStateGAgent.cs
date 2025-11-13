namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Stateful GAgent interface
/// </summary>
/// <typeparam name="TState">Agent state type</typeparam>
public interface IStateGAgent<TState> : IGAgent
    where TState : class
{
    /// <summary>
    /// Get current state (read-only)
    /// </summary>
    TState GetState();
}
