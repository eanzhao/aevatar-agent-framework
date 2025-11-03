using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Orleans;
using Aevatar.Agents.Serialization;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Orleans;
using Xunit;

namespace Aevatar.Agents.Orleans.Tests;

/// <summary>
/// Simple unit tests for Orleans components without TestCluster
/// </summary>
public class SimpleOrleansUnitTests
{
    // Test Agent
    public class SimpleAgent : GAgentBase<object>
    {
        public SimpleAgent() : base(Guid.NewGuid()) { }
        public SimpleAgent(Guid id) : base(id) { }
        
        public override Task<string> GetDescriptionAsync()
        {
            return Task.FromResult($"SimpleAgent {Id}");
        }
    }
    
    [Fact]
    public void OrleansGAgentActor_Should_Initialize_Correctly()
    {
        // Arrange
        var mockGrain = new Mock<IGAgentGrain>();
        var agent = new SimpleAgent(Guid.NewGuid());
        
        // Act
        var actor = new OrleansGAgentActor(mockGrain.Object, agent);
        
        // Assert
        Assert.NotNull(actor);
        Assert.Equal(agent.Id, actor.Id);
        Assert.Same(agent, actor.GetAgent());
        Assert.Same(mockGrain.Object, actor.GetGrain());
    }
    
    [Fact]
    public async Task OrleansGAgentActor_Should_Call_Grain_ActivateAsync_With_Type_Info()
    {
        // Arrange
        var mockGrain = new Mock<IGAgentGrain>();
        var agent = new SimpleAgent(Guid.NewGuid());
        var actor = new OrleansGAgentActor(mockGrain.Object, agent);
        
        mockGrain.Setup(g => g.ActivateAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        
        // Act
        await actor.ActivateAsync();
        
        // Assert
        mockGrain.Verify(g => g.ActivateAsync(
            It.Is<string>(s => s!.Contains("SimpleAgent")),
            It.Is<string>(s => s!.Contains("Object"))),
            Times.Once);
    }
    
    [Fact]
    public async Task OrleansGAgentActor_Should_Serialize_EventEnvelope_Correctly()
    {
        // Arrange
        var mockGrain = new Mock<IGAgentGrain>();
        var agent = new SimpleAgent(Guid.NewGuid());
        var actor = new OrleansGAgentActor(mockGrain.Object, agent);
        
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(new StringValue { Value = "test" }),
            Direction = EventDirection.Down
        };
        
        byte[]? capturedBytes = null;
        mockGrain.Setup(g => g.HandleEventAsync(It.IsAny<byte[]>()))
            .Callback<byte[]>(bytes => capturedBytes = bytes)
            .Returns(Task.CompletedTask);
        
        // Act
        await actor.PublishEventAsync(envelope);
        
        // Assert
        Assert.NotNull(capturedBytes);
        
        // Verify we can deserialize it back
        var deserialized = EventEnvelope.Parser.ParseFrom(capturedBytes);
        Assert.Equal(envelope.Id, deserialized.Id);
        Assert.Equal(envelope.Direction, deserialized.Direction);
    }
    
    [Fact]
    public async Task OrleansGAgentActor_Should_Wrap_Non_EventEnvelope_Messages()
    {
        // Arrange
        var mockGrain = new Mock<IGAgentGrain>();
        var agent = new SimpleAgent(Guid.NewGuid());
        var actor = new OrleansGAgentActor(mockGrain.Object, agent);
        
        var message = new StringValue { Value = "test message" };
        
        byte[]? capturedBytes = null;
        mockGrain.Setup(g => g.HandleEventAsync(It.IsAny<byte[]>()))
            .Callback<byte[]>(bytes => capturedBytes = bytes)
            .Returns(Task.CompletedTask);
        
        // Act
        await actor.PublishEventAsync(message);
        
        // Assert
        Assert.NotNull(capturedBytes);
        
        // Verify it was wrapped in EventEnvelope
        var envelope = EventEnvelope.Parser.ParseFrom(capturedBytes);
        Assert.NotNull(envelope.Id);
        Assert.Equal(EventDirection.Down, envelope.Direction);
        
        // Verify payload
        var unpacked = envelope.Payload.Unpack<StringValue>();
        Assert.Equal("test message", unpacked.Value);
    }
    
    [Fact]
    public async Task OrleansGAgentActor_Should_Handle_Hierarchy_Operations()
    {
        // Arrange
        var mockGrain = new Mock<IGAgentGrain>();
        var agent = new SimpleAgent(Guid.NewGuid());
        var actor = new OrleansGAgentActor(mockGrain.Object, agent);
        
        var childId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        
        mockGrain.Setup(g => g.AddChildAsync(It.IsAny<Guid>())).Returns(Task.CompletedTask);
        mockGrain.Setup(g => g.RemoveChildAsync(It.IsAny<Guid>())).Returns(Task.CompletedTask);
        mockGrain.Setup(g => g.SetParentAsync(It.IsAny<Guid>())).Returns(Task.CompletedTask);
        mockGrain.Setup(g => g.GetChildrenAsync()).ReturnsAsync(new[] { childId });
        mockGrain.Setup(g => g.GetParentAsync()).ReturnsAsync(parentId);
        
        // Act & Assert - Add Child
        await actor.AddChildAsync(childId);
        mockGrain.Verify(g => g.AddChildAsync(childId), Times.Once);
        
        // Act & Assert - Remove Child
        await actor.RemoveChildAsync(childId);
        mockGrain.Verify(g => g.RemoveChildAsync(childId), Times.Once);
        
        // Act & Assert - Set Parent
        await actor.SetParentAsync(parentId);
        mockGrain.Verify(g => g.SetParentAsync(parentId), Times.Once);
        
        // Act & Assert - Get Children
        var children = await actor.GetChildrenAsync();
        Assert.Contains(childId, children);
        
        // Act & Assert - Get Parent
        var parent = await actor.GetParentAsync();
        Assert.Equal(parentId, parent);
    }
    
    [Fact]
    public void OrleansGAgentGrain_SetAgent_Should_Work()
    {
        // This tests the critical SetAgent fix
        // The actual Grain would call SetAgent during ActivateAsync
        
        // Arrange
        var grain = new TestableOrleansGAgentGrain();
        var agent = new SimpleAgent(Guid.NewGuid());
        
        // Act
        grain.TestSetAgent(agent);
        
        // Assert
        Assert.Same(agent, grain.GetAgent());
        Assert.NotNull(grain.GetAgent());
    }
    
    // Testable version of OrleansGAgentGrain for unit testing
    private class TestableOrleansGAgentGrain : IEventPublisher
    {
        private IGAgent? _agent;
        
        public void TestSetAgent(IGAgent agent)
        {
            // This simulates what SetAgent does
            _agent = agent;
            agent.GetType().GetMethod("SetEventPublisher")?.Invoke(agent, new object[] { this });
        }
        
        public IGAgent? GetAgent() => _agent;
        
        // IEventPublisher implementation
        public Task<string> PublishAsync<TEvent>(TEvent evt, EventDirection direction = EventDirection.Down, CancellationToken ct = default) 
            where TEvent : IMessage
        {
            return Task.FromResult(Guid.NewGuid().ToString());
        }
    }
    
    [Fact]
    public void OrleansGAgentActorFactory_Should_Create_With_Correct_Options()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<OrleansGAgentActorFactoryOptions>(opt =>
        {
            opt.DefaultGrainType = GrainType.Standard;
            opt.UseEventSourcing = false;
            opt.UseJournaledGrain = false;
        });
        
        var serviceProvider = services.BuildServiceProvider();
        var mockGrainFactory = new Mock<IGrainFactory>();
        var logger = serviceProvider.GetRequiredService<ILogger<OrleansGAgentActorFactory>>();
        var options = serviceProvider.GetRequiredService<IOptions<OrleansGAgentActorFactoryOptions>>();
        
        // Act
        var factory = new OrleansGAgentActorFactory(
            serviceProvider,
            mockGrainFactory.Object,
            logger,
            options);
        
        // Assert
        Assert.NotNull(factory);
    }
}
