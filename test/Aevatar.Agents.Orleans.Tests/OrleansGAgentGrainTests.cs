using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Core;
using Aevatar.Agents.Orleans;
using Aevatar.Agents.Orleans.EventSourcing;
using Aevatar.Agents.Serialization;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Hosting;
using Orleans.TestingHost;
using Xunit;

namespace Aevatar.Agents.Orleans.Tests;

// Test Agent State
public partial class TestAgentState : IMessage<TestAgentState>
{
    public string Name { get; set; } = string.Empty;
    public int Counter { get; set; }
    public Timestamp LastUpdate { get; set; } = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
    
    // IMessage implementation
    public void MergeFrom(TestAgentState message)
    {
        if (message == null) return;
        Name = message.Name;
        Counter = message.Counter;
        LastUpdate = message.LastUpdate;
    }
    
    public void MergeFrom(CodedInputStream input)
    {
        // Simplified implementation
    }
    
    public void WriteTo(CodedOutputStream output)
    {
        // Simplified implementation
    }
    
    public int CalculateSize() => 0;
    
    public MessageDescriptor Descriptor => null!;
    
    public TestAgentState Clone() => new TestAgentState 
    { 
        Name = Name, 
        Counter = Counter, 
        LastUpdate = LastUpdate?.Clone() 
    };
    
    public bool Equals(TestAgentState? other) => other != null && 
        Name == other.Name && 
        Counter == other.Counter;
}

// Test Agent
public class TestAgent : GAgentBase<TestAgentState>
{
    public bool WasActivated { get; private set; }
    public bool WasDeactivated { get; private set; }
    
    public TestAgent() : base(Guid.NewGuid())
    {
    }
    
    public TestAgent(Guid id) : base(id)
    {
    }
    
    public override async Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        WasActivated = true;
        await base.OnActivateAsync(cancellationToken);
    }
    
    public override async Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        WasDeactivated = true;
        await base.OnDeactivateAsync(cancellationToken);
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"TestAgent {Id}");
    }
    
    [EventHandler]
    public async Task HandleTestEvent(EventEnvelope envelope)
    {
        GetState().Counter++;
        GetState().LastUpdate = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
        await Task.CompletedTask;
    }
}

// Test Event Sourcing Agent
public class TestEventSourcingAgent : GAgentBase<TestAgentState>, IEventSourcingAgent
{
    public bool RequiresEventSourcing => true;
    
    public TestEventSourcingAgent() : base(Guid.NewGuid())
    {
    }
    
    public TestEventSourcingAgent(Guid id) : base(id)
    {
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"TestEventSourcingAgent {Id}");
    }
}

public class OrleansGAgentGrainTests : IClassFixture<OrleansGAgentGrainTests.ClusterFixture>
{
    private readonly TestCluster _cluster;
    private readonly IGrainFactory _grainFactory;
    
    public OrleansGAgentGrainTests(ClusterFixture fixture)
    {
        _cluster = fixture.Cluster;
        _grainFactory = _cluster.GrainFactory;
    }
    
    [Fact]
    public async Task Grain_Should_Call_SetAgent_During_Activation()
    {
        // Arrange
        var agentId = Guid.NewGuid().ToString();
        var grain = _grainFactory.GetGrain<IStandardGAgentGrain>(agentId);
        
        // Act - Pass agent type information
        await grain.ActivateAsync(
            typeof(TestAgent).AssemblyQualifiedName,
            typeof(TestAgentState).AssemblyQualifiedName);
        
        // Assert - Verify agent was created and initialized
        var id = await grain.GetIdAsync();
        Assert.Equal(agentId, id.ToString());
        
        // Send an event to verify agent is working
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(new StringValue { Value = "test" }),
            Direction = EventDirection.Down
        };
        
        using var stream = new MemoryStream();
        envelope.WriteTo(stream);
        
        await grain.HandleEventAsync(stream.ToArray());
        
        // If no exception, agent was properly initialized
    }
    
    [Fact]
    public async Task Grain_Should_Fail_Without_Agent_Set()
    {
        // Arrange
        var agentId = Guid.NewGuid().ToString();
        var grain = _grainFactory.GetGrain<IStandardGAgentGrain>(agentId);
        
        // Act - Activate without agent type (simulating the bug)
        await grain.ActivateAsync();
        
        // Assert - Should throw when trying to handle event
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(new StringValue { Value = "test" }),
            Direction = EventDirection.Down
        };
        
        using var stream = new MemoryStream();
        envelope.WriteTo(stream);
        
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await grain.HandleEventAsync(stream.ToArray()));
    }
    
    [Fact]
    public async Task Should_Select_Correct_Grain_Type_For_Standard_Agent()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IGrainFactory>(_grainFactory);
        services.AddSingleton<ILogger<OrleansGAgentActorFactory>>(
            new Logger<OrleansGAgentActorFactory>(new LoggerFactory()));
        services.Configure<OrleansGAgentActorFactoryOptions>(opt =>
        {
            opt.DefaultGrainType = GrainType.Standard;
        });
        
        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<OrleansGAgentActorFactoryOptions>>();
        
        var factory = new OrleansGAgentActorFactory(
            serviceProvider,
            _grainFactory,
            serviceProvider.GetRequiredService<ILogger<OrleansGAgentActorFactory>>(),
            options);
        
        // Act
        var actor = await factory.CreateAgentAsync<TestAgent, TestAgentState>(Guid.NewGuid());
        
        // Assert
        Assert.NotNull(actor);
        Assert.IsType<OrleansGAgentActor>(actor);
    }
    
    [Fact]
    public async Task Should_Select_EventSourcing_Grain_For_EventSourcing_Agent()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IGrainFactory>(_grainFactory);
        services.AddSingleton<ILogger<OrleansGAgentActorFactory>>(
            new Logger<OrleansGAgentActorFactory>(new LoggerFactory()));
        services.Configure<OrleansGAgentActorFactoryOptions>(opt =>
        {
            opt.UseEventSourcing = true;
            opt.DefaultGrainType = GrainType.EventSourcing;
        });
        
        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<OrleansGAgentActorFactoryOptions>>();
        
        var factory = new OrleansGAgentActorFactory(
            serviceProvider,
            _grainFactory,
            serviceProvider.GetRequiredService<ILogger<OrleansGAgentActorFactory>>(),
            options);
        
        // Act & Assert - Should not throw
        var actor = await factory.CreateAgentAsync<TestEventSourcingAgent, TestAgentState>(Guid.NewGuid());
        
        Assert.NotNull(actor);
    }
    
    [Fact]
    public async Task PublishEventAsync_Should_Work_Through_Grain()
    {
        // Arrange
        var agentId = Guid.NewGuid().ToString();
        var grain = _grainFactory.GetGrain<IStandardGAgentGrain>(agentId);
        
        await grain.ActivateAsync(
            typeof(TestAgent).AssemblyQualifiedName,
            typeof(TestAgentState).AssemblyQualifiedName);
        
        // Create actor wrapper
        var actor = new OrleansGAgentActor(grain, new TestAgent(Guid.Parse(agentId)));
        await actor.ActivateAsync();
        
        // Act - Publish event through actor
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(new StringValue { Value = "test" }),
            Direction = EventDirection.Down
        };
        
        await actor.PublishEventAsync(envelope);
        
        // Assert - If no exception, event was published successfully
    }
    
    [Fact]
    public async Task Grain_Should_Handle_Concurrent_Events()
    {
        // Arrange
        var agentId = Guid.NewGuid().ToString();
        var grain = _grainFactory.GetGrain<IStandardGAgentGrain>(agentId);
        
        await grain.ActivateAsync(
            typeof(TestAgent).AssemblyQualifiedName,
            typeof(TestAgentState).AssemblyQualifiedName);
        
        // Act - Send multiple events concurrently
        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            var envelope = new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Payload = Any.Pack(new StringValue { Value = $"test_{i}" }),
                Direction = EventDirection.Down
            };
            
            using var stream = new MemoryStream();
            envelope.WriteTo(stream);
            
            tasks.Add(grain.HandleEventAsync(stream.ToArray()));
        }
        
        await Task.WhenAll(tasks);
        
        // Assert - All events should be processed without error
        Assert.True(tasks.All(t => t.IsCompletedSuccessfully));
    }
    
    [Fact]
    public async Task Grain_Should_Properly_Serialize_Protobuf_Messages()
    {
        // Arrange
        var agentId = Guid.NewGuid().ToString();
        var grain = _grainFactory.GetGrain<IStandardGAgentGrain>(agentId);
        
        await grain.ActivateAsync(
            typeof(TestAgent).AssemblyQualifiedName,
            typeof(TestAgentState).AssemblyQualifiedName);
        
        // Act - Send complex protobuf message
        var complexPayload = new Any
        {
            TypeUrl = "test.complex",
            Value = ByteString.CopyFromUtf8("complex_data")
        };
        
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = complexPayload,
            Direction = EventDirection.Bidirectional,
            MaxHopCount = 5,
            CurrentHopCount = 2,
            CorrelationId = "correlation_123"
        };
        
        using var stream = new MemoryStream();
        envelope.WriteTo(stream);
        
        await grain.HandleEventAsync(stream.ToArray());
        
        // Assert - Should handle complex message without error
    }
    
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
            Cluster?.StopAllSilos();
        }
        
        private class TestSiloConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder siloBuilder)
            {
                siloBuilder
                    .UseLocalhostClustering()
                    .AddMemoryGrainStorage("PubSubStore")
                    .AddMemoryStreams("StreamProvider")
                    .AddMemoryGrainStorage("Default")
                    .ConfigureLogging(logging => logging.AddConsole());
            }
        }
    }
}
