namespace Aevatar.Agents.Abstractions.Persistence;

/// <summary>
/// Configuration store interface for agent-specific configurations
/// Allows each agent instance to have its own configuration
/// </summary>
/// <typeparam name="TConfig">Configuration type</typeparam>
public interface IConfigStore<TConfig>
    where TConfig : class, new()
{
    /// <summary>
    /// Load agent configuration
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Configuration object, or null if not exists</returns>
    Task<TConfig?> LoadAsync(Guid agentId, CancellationToken ct = default);

    /// <summary>
    /// Save agent configuration
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="config">Configuration object</param>
    /// <param name="ct">Cancellation token</param>
    Task SaveAsync(Guid agentId, TConfig config, CancellationToken ct = default);

    /// <summary>
    /// Delete agent configuration
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="ct">Cancellation token</param>
    Task DeleteAsync(Guid agentId, CancellationToken ct = default);

    /// <summary>
    /// Check if configuration exists
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="ct">Cancellation token</param>
    Task<bool> ExistsAsync(Guid agentId, CancellationToken ct = default);
}
