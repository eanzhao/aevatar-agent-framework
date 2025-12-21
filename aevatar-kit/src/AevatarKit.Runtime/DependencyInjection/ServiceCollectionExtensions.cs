using Microsoft.Extensions.DependencyInjection;
using AevatarKit.Runtime.Run;

namespace AevatarKit.Runtime.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAevatarKitRuntime(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IRunRegistry, InMemoryRunRegistry>();
        services.AddSingleton<IRunEngine, MvpRunEngine>();

        return services;
    }
}


