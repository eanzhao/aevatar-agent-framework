using System;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Orleans;
using Aevatar.Agents.Serialization;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Hosting;
using Orleans.TestingHost;
using Xunit;

namespace Aevatar.Agents.Orleans.Tests;

/// <summary>
/// Basic Orleans tests to verify SetAgent and core functionality
/// </summary>
public class BasicOrleansTests : IClassFixture<BasicOrleansTests.TestClusterFixture>
{
    private readonly TestCluster _cluster;
    private readonly IGrainFactory _grainFactory;
    private readonly IServiceProvider _serviceProvider;
    
    public BasicOrleansTests(TestClusterFixture fixture)
    {
        _cluster = fixture.Cluster;
        _grainFactory = _cluster.GrainFactory;
        
        // Setup service provider
        var services = new ServiceCollection();
        services.AddSingleton<IGrainFactory>(_grainFactory);
        services.AddLogging();
        services.Configure<OrleansGAgentActorFactoryOptions>(opt =>
        {
            opt.DefaultGrainType = GrainType.Standard;
        });
        
        _serviceProvider = services.BuildServiceProvider();
    }
    
    // Simple test agent
    public class TestAgent : GAgentBase<TestState>
    {
        public bool WasActivated { get; private set; }
        
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
        
        public override Task<string> GetDescriptionAsync()
        {
            return Task.FromResult($"TestAgent {Id}");
        }
        
        [EventHandler]
        public async Task HandleTestEvent(TestEvent evt)
        {
            GetState().Counter++;
            GetState().LastMessage = evt.Message;
            await Task.CompletedTask;
        }
    }
    
    // Test state
    public class TestState
    {
        public int Counter { get; set; }
        public string LastMessage { get; set; } = "";
    }
    
    // Test event
    public class TestEvent : IMessage
    {
        public string Message { get; set; } = "";
        
        public void MergeFrom(TestEvent message) { }
        public void MergeFrom(CodedInputStream input) { }
        public void WriteTo(CodedOutputStream output) { }
        public int CalculateSize() => 0;
        public MessageDescriptor Descriptor => null!;
        public TestEvent Clone() => new TestEvent { Message = Message };
        public bool Equals(TestEvent? other) => other?.Message == Message;
    }
    
    [Fact]
    public async Task SetAgent_Should_Be_Called_During_Grain_Activation()
    {
        // This is the critical test - verifies SetAgent is called
        
        // Arrange
        var agentId = Guid.NewGuid().ToString();
        var grain = _grainFactory.GetGrain<IStandardGAgentGrain>(agentId);
        
        // Act - Activate Grain with Agent type info
        await grain.ActivateAsync(
            typeof(TestAgent).AssemblyQualifiedName,
            typeof(TestState).AssemblyQualifiedName);
        
        // Assert - Send event to verify Agent was set
        var testEnvelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(new StringValue { Value = "test" }),
            Direction = EventDirection.Down
        };
        
        using var stream = new System.IO.MemoryStream();
        testEnvelope.WriteTo(stream);
        
        // Should not throw "Agent not set" exception
        Exception? caughtException = null;
        try
        {
            await grain.HandleEventAsync(stream.ToArray());
        }
        catch (Exception ex)
        {
            caughtException = ex;
        }
        
        // Verify no exception (Agent was properly set)
        Assert.Null(caughtException);
    }
    
    [Fact]
    public async Task Without_Agent_Type_Should_Fail()
    {
        // Verify that without Agent type, it should fail
        
        // Arrange
        var agentId = Guid.NewGuid().ToString();
        var grain = _grainFactory.GetGrain<IStandardGAgentGrain>(agentId);
        
        // Act - Activate without Agent type (simulating the bug)
        await grain.ActivateAsync();
        
        // Assert - Should fail when handling event
        var testEnvelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(new StringValue { Value = "test" }),
            Direction = EventDirection.Down
        };
        
        using var stream = new System.IO.MemoryStream();
        testEnvelope.WriteTo(stream);
        
        // Should throw "Agent not set" exception
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await grain.HandleEventAsync(stream.ToArray()));
    }
    
    [Fact]
    public async Task Factory_Should_Create_Working_Actor()
    {
        // Test factory creates properly initialized actors
        
        // Arrange
        var logger = _serviceProvider.GetRequiredService<ILogger<OrleansGAgentActorFactory>>();
        var options = _serviceProvider.GetService<IOptions<OrleansGAgentActorFactoryOptions>>();
        
        var factory = new OrleansGAgentActorFactory(
            _serviceProvider,
            _grainFactory,
            logger,
            options);
        
        // Act
        var agentId = Guid.NewGuid();
        var actor = await factory.CreateAgentAsync<TestAgent, TestState>(agentId);
        
        // Assert
        Assert.NotNull(actor);
        Assert.Equal(agentId, actor.Id);
        
        // Send event to verify it works
        var testEvent = new TestEvent { Message = "test" };
        var eventId = await actor.PublishEventAsync(testEvent);
        
        Assert.NotNull(eventId);
    }
    
    [Fact]
    public async Task Multiple_Agents_Should_Work_Independently()
    {
        // Test multiple agents work without interference
        
        // Arrange
        var logger = _serviceProvider.GetRequiredService<ILogger<OrleansGAgentActorFactory>>();
        var options = _serviceProvider.GetService<IOptions<OrleansGAgentActorFactoryOptions>>();
        
        var factory = new OrleansGAgentActorFactory(
            _serviceProvider,
            _grainFactory,
            logger,
            options);
        
        // Act - Create multiple agents
        var agent1Id = Guid.NewGuid();
        var agent2Id = Guid.NewGuid();
        
        var actor1 = await factory.CreateAgentAsync<TestAgent, TestState>(agent1Id);
        var actor2 = await factory.CreateAgentAsync<TestAgent, TestState>(agent2Id);
        
        // Assert - Both should work independently
        Assert.NotNull(actor1);
        Assert.NotNull(actor2);
        Assert.Equal(agent1Id, actor1.Id);
        Assert.Equal(agent2Id, actor2.Id);
        
        // Send different events
        await actor1.PublishEventAsync(new TestEvent { Message = "agent1" });
        await actor2.PublishEventAsync(new TestEvent { Message = "agent2" });
        
        // Both should handle their events without errors
    }
    
    [Fact]
    public async Task Hierarchical_Relationships_Should_Work()
    {
        // Test parent-child relationships
        
        // Arrange
        var logger = _serviceProvider.GetRequiredService<ILogger<OrleansGAgentActorFactory>>();
        var options = _serviceProvider.GetService<IOptions<OrleansGAgentActorFactoryOptions>>();
        
        var factory = new OrleansGAgentActorFactory(
            _serviceProvider,
            _grainFactory,
            logger,
            options);
        
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        
        // Act
        var parent = await factory.CreateAgentAsync<TestAgent, TestState>(parentId);
        var child = await factory.CreateAgentAsync<TestAgent, TestState>(childId);
        
        await parent.AddChildAsync(childId);
        await child.SetParentAsync(parentId);
        
        // Assert
        var children = await parent.GetChildrenAsync();
        Assert.Contains(childId, children);
        
        var parentOfChild = await child.GetParentAsync();
        Assert.Equal(parentId, parentOfChild);
    }
    
    [Fact]
    public async Task Event_Routing_Should_Respect_Direction()
    {
        // Test event routing with different directions
        
        // Arrange
        var logger = _serviceProvider.GetRequiredService<ILogger<OrleansGAgentActorFactory>>();
        var options = _serviceProvider.GetService<IOptions<OrleansGAgentActorFactoryOptions>>();
        
        var factory = new OrleansGAgentActorFactory(
            _serviceProvider,
            _grainFactory,
            logger,
            options);
        
        var agentId = Guid.NewGuid();
        var actor = await factory.CreateAgentAsync<TestAgent, TestState>(agentId);
        
        // Act - Send events with different directions
        await actor.PublishEventAsync(new TestEvent { Message = "down" }, EventDirection.Down);
        await actor.PublishEventAsync(new TestEvent { Message = "up" }, EventDirection.Up);
        await actor.PublishEventAsync(new TestEvent { Message = "bidirectional" }, EventDirection.Bidirectional);
        
        // Assert - All should be handled without errors
    }
    
    public class TestClusterFixture : IDisposable
    {
        public TestCluster Cluster { get; }
        
        public TestClusterFixture()
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
                    .ConfigureLogging(logging => logging
                        .AddConsole()
                        .SetMinimumLevel(LogLevel.Warning));
            }
        }
    }
}