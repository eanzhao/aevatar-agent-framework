using Aevatar.Agents.Core.DependencyInjection;
using Proto;

namespace Aevatar.Agents.Runtime.ProtoActor;

/// <summary>
/// <see cref="IAevatarBuilder"/> helpers for ProtoActor runtime wiring.
/// </summary>
public static class AevatarBuilderExtensions
{
    public static IAevatarBuilder UseProtoActorRuntime(
        this IAevatarBuilder builder,
        Action<ActorSystemConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        EnsureRuntimeNotConfigured(builder);

        builder.Services.AddAevatarProtoActorRuntime(configure);
        builder.Properties[AevatarBuilderPropertyKeys.Runtime] = "ProtoActor";

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

