using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Hosting;
using Orleans.TestingHost;
using Orleans.Serialization;

namespace Aevatar.Agents.TestBase;

/// <summary>
/// Shared Orleans test cluster fixture for all agent tests
/// </summary>
public class ClusterFixture : IDisposable
{
    public TestCluster Cluster { get; }

    public ClusterFixture()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TestSiloConfigurations>();
        builder.AddClientBuilderConfigurator<TestClientBuilderConfigurator>();
        Cluster = builder.Build();
        Cluster.Deploy();
    }

    public void Dispose()
    {
        Cluster?.StopAllSilos();
    }

    private class TestSiloConfigurations : ISiloConfigurator
    {
        public void Configure(ISiloBuilder hostBuilder)
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile("appsettings.secrets.json", optional: true)
                .Build();

            hostBuilder
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IConfiguration>(configuration);
                    
                    // Add logging
                    services.AddLogging(logging =>
                    {
                        logging.AddConsole();
                        logging.SetMinimumLevel(LogLevel.Warning);
                    });
                    
                    // Register in-memory EventSourcing for testing
                    services.AddInMemoryEventSourcing();
                    
                    services.AddSerializer(serializerBuilder =>
                    {
                        serializerBuilder.AddProtobufSerializer();
                    });
                })
                .AddMemoryStreams("StreamProvider")
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryGrainStorage("EventStoreStorage")
                .AddMemoryGrainStorageAsDefault()
                .AddLogStorageBasedLogConsistencyProvider("LogStorage");
        }
    }

    private class TestClientBuilderConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder
                .AddMemoryStreams("StreamProvider")
                .ConfigureServices(services =>
                {
                    // Register in-memory EventSourcing for testing
                    services.AddInMemoryEventSourcing();
                    
                    services.AddSerializer(serializerBuilder =>
                    {
                        serializerBuilder.AddProtobufSerializer();
                    });
                });
        }
    }
}
