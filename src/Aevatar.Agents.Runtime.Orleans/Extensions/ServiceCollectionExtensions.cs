using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aevatar.Agents.Runtime.Orleans.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Aevatar Agent System using Orleans Runtime.
    /// Pre-requisite: You must configure Orleans Host (silo/client) separately.
    /// </summary>
    public static IServiceCollection AddAevatarOrleansRuntime(this IServiceCollection services)
    {
        services.AddSingleton<IGAgentActorFactory, OrleansGAgentActorFactory>();
        services.AddSingleton<IGAgentActorManager, OrleansGAgentActorManager>();
        services.TryAddSingleton<IGAgentFactory, AIGAgentFactory>();
        return services;
    }
}