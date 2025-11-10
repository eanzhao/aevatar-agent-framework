using System.Threading.Tasks;
using Aevatar.Agents.AI.Core.Models;

namespace Aevatar.Agents.AI.Core.Strategies;

/// <summary>
/// Defines a processing strategy for handling AI chat requests.
/// </summary>
public interface IProcessingStrategy
{
    /// <summary>
    /// Gets the name of this processing strategy.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the description of what this strategy does.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Determines whether this strategy can handle the given request.
    /// </summary>
    /// <param name="request">The chat request to evaluate.</param>
    /// <returns>True if this strategy can handle the request; otherwise, false.</returns>
    bool CanHandle(ChatRequest request);

    /// <summary>
    /// Processes a chat request using this strategy.
    /// </summary>
    /// <param name="context">The processing context containing all necessary information.</param>
    /// <returns>The processing result.</returns>
    Task<ProcessingResult> ProcessAsync(ProcessingContext context);

    /// <summary>
    /// Gets the estimated complexity score for processing the given request.
    /// </summary>
    /// <param name="request">The chat request to evaluate.</param>
    /// <returns>A complexity score from 0 (simple) to 1 (complex).</returns>
    double EstimateComplexity(ChatRequest request);

    /// <summary>
    /// Validates that this strategy has all required dependencies configured.
    /// </summary>
    /// <param name="context">The processing context to validate.</param>
    /// <returns>True if all requirements are met; otherwise, false.</returns>
    bool ValidateRequirements(ProcessingContext context);
}
