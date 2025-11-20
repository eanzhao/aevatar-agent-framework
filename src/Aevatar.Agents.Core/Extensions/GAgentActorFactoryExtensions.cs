using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Factory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aevatar.Agents.Core.Extensions;

public static class GAgentActorFactoryExtensions
{
    public static IServiceCollection AddGAgentActorFactoryProvider(this IServiceCollection services)
    {
        services.TryAddSingleton<IGAgentActorFactoryProvider, DefaultGAgentActorFactoryProvider>();
        return services;
    }
}