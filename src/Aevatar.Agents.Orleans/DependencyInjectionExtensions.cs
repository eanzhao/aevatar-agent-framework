using Aevatar.Agents.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Streams;

namespace Aevatar.Agents.Orleans;

/// <summary>
/// 依赖注入扩展方法
/// </summary>
public static class DependencyInjectionExtensions
{
    /// <summary>
    /// 添加Orleans实现的代理服务（不使用Orleans Streams）
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddOrleansAgents(this IServiceCollection services)
    {
        // 注册工厂
        services.AddSingleton<IGAgentFactory>(sp =>
        {
            var grainFactory = sp.GetRequiredService<IGrainFactory>();
            return new OrleansGAgentFactory(sp, grainFactory);
        });

        return services;
    }

    /// <summary>
    /// 添加Orleans实现的代理服务（使用Orleans Streams）
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="streamProviderName">流提供者名称（默认为"StreamProvider"）</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddOrleansAgentsWithStreams(
        this IServiceCollection services,
        string streamProviderName = "StreamProvider")
    {
        // 注册工厂
        services.AddSingleton<IGAgentFactory>(sp =>
        {
            var grainFactory = sp.GetRequiredService<IGrainFactory>();
            var clusterClient = sp.GetRequiredService<IClusterClient>();
            var streamProvider = clusterClient.GetStreamProvider(streamProviderName);
            return new OrleansGAgentFactory(sp, grainFactory, streamProvider);
        });

        return services;
    }
}

