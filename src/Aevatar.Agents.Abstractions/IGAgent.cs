namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Base interface for all GAgents
/// Defines the minimum abstraction with only identity
/// </summary>
public interface IGAgent
{
    /// <summary>
    /// Agent unique identifier (Guid type, universal identifier)
    /// </summary>
    Guid Id { get; }

    /// <summary>
    /// Get GAgent description.
    /// </summary>
    /// <returns>A descriptive string about this agent</returns>
    Task<string> GetDescriptionAsync();

    /// <summary>
    /// Get all subscribed events of current GAgent.
    /// </summary>
    /// <param name="includeAllEventHandler">Whether to include AllEventHandler subscribed events</param>
    /// <returns>List of event types this agent can handle</returns>
    Task<List<Type>> GetAllSubscribedEventsAsync(bool includeAllEventHandler = false);

    /// <summary>
    /// Active current GAgent.
    /// </summary>
    /// <returns></returns>
    Task ActivateAsync();

    /// <summary>
    /// Deactive current GAgent.
    /// </summary>
    /// <returns></returns>
    Task DeactivateAsync();
}
