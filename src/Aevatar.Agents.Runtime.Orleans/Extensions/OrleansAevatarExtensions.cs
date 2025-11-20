using Aevatar.Agents.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.Runtime.Orleans.Extensions;

public static class OrleansAevatarExtensions
{
    /// <summary>
    /// Adds Aevatar Agent System using Orleans Runtime.
    /// Pre-requisite: You must configure Orleans Host (silo/client) separately.
    /// </summary>
    public static IServiceCollection AddAevatarOrleansRuntime(this IServiceCollection services)
    {
        services.AddSingleton<IGAgentActorFactory, OrleansGAgentActorFactory>();
        services.AddSingleton<IGAgentActorManager, OrleansGAgentActorManager>();
        return services;
    }
}