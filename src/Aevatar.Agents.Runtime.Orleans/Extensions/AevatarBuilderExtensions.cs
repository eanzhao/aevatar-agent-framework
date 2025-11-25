using Aevatar.Agents.Core.DependencyInjection;

namespace Aevatar.Agents.Runtime.Orleans.Extensions;

/// <summary>
/// Fluent helpers for wiring the Orleans runtime.
/// </summary>
public static class AevatarBuilderExtensions
{
    public static IAevatarBuilder UseOrleansRuntime(this IAevatarBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        EnsureRuntimeNotConfigured(builder);

        builder.Services.AddAevatarOrleansRuntime();
        builder.Properties[AevatarBuilderPropertyKeys.Runtime] = "Orleans";

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

