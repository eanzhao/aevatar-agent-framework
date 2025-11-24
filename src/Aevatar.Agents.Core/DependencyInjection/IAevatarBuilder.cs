using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.Core.DependencyInjection;

/// <summary>
/// Fluent builder used to configure the Aevatar Agent IoC container.
/// </summary>
public interface IAevatarBuilder
{
    /// <summary>
    /// Underlying service collection.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Shared property bag that extensions can use to store metadata.
    /// </summary>
    IDictionary<string, object> Properties { get; }
}