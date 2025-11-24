namespace Aevatar.Agents.Core.DependencyInjection;

/// <summary>
/// Shared property keys used by <see cref="IAevatarBuilder"/> extensions.
/// </summary>
public static class AevatarBuilderPropertyKeys
{
    public const string Runtime = "Aevatar.Runtime";
    public const string StateStore = "Aevatar.StateStore";
    public const string ConfigStore = "Aevatar.ConfigStore";
    public const string EventStore = "Aevatar.EventStore";
}

