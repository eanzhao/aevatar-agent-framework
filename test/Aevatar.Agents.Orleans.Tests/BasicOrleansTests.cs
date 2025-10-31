using Aevatar.Agents.Orleans;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Hosting;
using Orleans.TestingHost;
using Xunit;

namespace Aevatar.Agents.Orleans.Tests;

/// <summary>
/// 基础Orleans测试 - 使用TestCluster
/// </summary>
[Collection(ClusterCollection.Name)]
public class BasicOrleansTests
{
    private readonly TestCluster _cluster;
    private readonly IGrainFactory _grainFactory;
    
    public BasicOrleansTests(ClusterFixture fixture)
    {
        _cluster = fixture.Cluster;
        _grainFactory = _cluster.GrainFactory;
    }
    
    [Fact]
    public async Task TestCluster_Should_BeOperational()
    {
        // Arrange & Act
        var grainId = Guid.NewGuid();
        var grain = _grainFactory.GetGrain<IGAgentGrain>(grainId.ToString());
        
        // Assert
        Assert.NotNull(grain);
        
        // Try to get ID (will activate the grain)
        var id = await grain.GetIdAsync();
        Assert.Equal(grainId, id);
    }
    
    [Fact]
    public async Task Grain_Should_MaintainState()
    {
        // Arrange
        var grainId = Guid.NewGuid();
        var grain1 = _grainFactory.GetGrain<IGAgentGrain>(grainId.ToString());
        
        // Act - Set parent
        var parentId = Guid.NewGuid();
        await grain1.SetParentAsync(parentId);
        
        // Get same grain again (should be same instance in Orleans)
        var grain2 = _grainFactory.GetGrain<IGAgentGrain>(grainId.ToString());
        var retrievedParentId = await grain2.GetParentAsync();
        
        // Assert
        Assert.Equal(parentId, retrievedParentId);
    }
    
    [Fact]
    public async Task MultipleGrains_Should_WorkConcurrently()
    {
        // Arrange
        var tasks = new List<Task>();
        var grainCount = 10;
        
        // Act - Create and activate multiple grains
        for (int i = 0; i < grainCount; i++)
        {
            var grainId = Guid.NewGuid();
            tasks.Add(Task.Run(async () =>
            {
                var grain = _grainFactory.GetGrain<IGAgentGrain>(grainId.ToString());
                var id = await grain.GetIdAsync();
                Assert.Equal(grainId, id);
            }));
        }
        
        // Assert
        await Task.WhenAll(tasks);
    }
}

/// <summary>
/// Cluster Fixture - 共享TestCluster实例
/// </summary>
public class ClusterFixture : IDisposable
{
    public TestCluster Cluster { get; }
    
    public ClusterFixture()
    {
        var builder = new TestClusterBuilder();
        
        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        
        Cluster = builder.Build();
        Cluster.Deploy();
    }
    
    public void Dispose()
    {
        Cluster.StopAllSilos();
    }
    
    private class TestSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .AddMemoryGrainStorage("Default")
                .AddMemoryGrainStorage("PubSubStore")  // Required for streams
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryStreams("TestStream")
                .ConfigureServices(services =>
                {
                    services.AddLogging(builder =>
                    {
                        builder.SetMinimumLevel(LogLevel.Warning);
                        builder.AddConsole();
                    });
                });
        }
    }
}

/// <summary>
/// Collection定义 - xUnit使用
/// </summary>
[CollectionDefinition(ClusterCollection.Name)]
public class ClusterCollection : ICollectionFixture<ClusterFixture>
{
    public const string Name = "ClusterCollection";
}
