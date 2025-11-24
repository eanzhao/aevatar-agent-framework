using Aevatar.Agents.Core.DependencyInjection;

namespace Aevatar.Agents.Runtime.Local;

/// <summary>
/// Fluent configuration helpers for wiring the Local runtime via <see cref="IAevatarBuilder"/>.
/// </summary>
public static class AevatarBuilderExtensions
{
    public static IAevatarBuilder UseLocalRuntime(this IAevatarBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        EnsureRuntimeNotConfigured(builder);

        builder.Services.AddAevatarLocalRuntime();
        builder.Properties[AevatarBuilderPropertyKeys.Runtime] = "Local";

        return builder;
    }

    private static void EnsureRuntimeNotConfigured(IAevatarBuilder builder)
    {
        if (builder.Properties.TryGetValue(AevatarBuilderPropertyKeys.Runtime, out var runtime))
        {
            throw new InvalidOperationException(
                $"Agent runtime has already been configured as {runtime}.");
        }
    }
}

