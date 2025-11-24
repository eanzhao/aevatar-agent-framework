using Aevatar.Agents.Abstractions.EventRouting;
using Aevatar.Agents.Abstractions.EventSourcing;

namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Global store registration options for the Aevatar Agent system.
/// </summary>
public class GAgentOptions
{
    /// <summary>
    /// Optional override for the <c>IStateStore&lt;&gt;</c> implementation. Must be an open generic type.
    /// </summary>
    public Type? StateStoreType { get; set; }

    /// <summary>
    /// Optional override for the <c>IConfigStore&lt;&gt;</c> implementation. Must be an open generic type.
    /// </summary>
    public Type? ConfigStoreType { get; set; }

    /// <summary>
    /// Optional override for <see cref="IEventStore"/>.
    /// </summary>
    public Type? EventStoreType { get; set; }

    /// <summary>
    /// Optional override for <see cref="IEventRouterStore"/>.
    /// </summary>
    public Type? EventRouterStoreType { get; set; }
}