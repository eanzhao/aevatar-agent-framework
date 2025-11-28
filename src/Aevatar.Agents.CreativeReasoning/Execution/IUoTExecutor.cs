using Aevatar.Agents.CreativeReasoning.Core;

namespace Aevatar.Agents.CreativeReasoning.Execution;

/// <summary>
/// Executor interface for Universe of Thoughts creative reasoning.
/// </summary>
public interface IUoTExecutor
{
    /// <summary>
    /// Execute C-UoT creative reasoning for a given problem.
    /// </summary>
    /// <param name="problem">The problem to solve creatively</param>
    /// <param name="options">Configuration options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Creative reasoning result with ranked solutions</returns>
    Task<UoTResult> ExecuteAsync(
        string problem,
        UoTOptions options,
        CancellationToken cancellationToken = default);
}

