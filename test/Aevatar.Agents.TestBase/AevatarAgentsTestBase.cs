using System;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Xunit;

namespace Aevatar.Agents.TestBase;

/// <summary>
/// Base class for all Aevatar Agents tests that need Orleans cluster
/// </summary>
public abstract class AevatarAgentsTestBase : IClassFixture<ClusterFixture>, IDisposable
{
    protected ClusterFixture Fixture { get; }
    protected IClusterClient ClusterClient { get; }
    protected IGrainFactory GrainFactory { get; }
    protected IServiceProvider ServiceProvider { get; }

    protected AevatarAgentsTestBase(ClusterFixture fixture)
    {
        Fixture = fixture;
        ClusterClient = Fixture.Cluster.Client;
        GrainFactory = Fixture.Cluster.GrainFactory;
        ServiceProvider = Fixture.Cluster.ServiceProvider;
    }

    public virtual void Dispose()
    {
        // Override in derived classes if needed
    }

    protected T GetRequiredService<T>() where T : notnull
    {
        return ServiceProvider.GetRequiredService<T>();
    }

    protected T? GetService<T>() where T : class
    {
        return ServiceProvider.GetService<T>();
    }
}




