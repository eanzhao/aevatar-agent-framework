using System;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Orleans;
using Aevatar.Agents.Serialization;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Aevatar.Agents.Orleans.Tests;

public class OrleansGAgentActorTests
{
    private readonly Mock<IGAgentGrain> _mockGrain;
    private readonly Mock<IGAgent> _mockAgent;
    private readonly OrleansGAgentActor _actor;
    
    public OrleansGAgentActorTests()
    {
        _mockGrain = new Mock<IGAgentGrain>();
        _mockAgent = new Mock<IGAgent>();
        _mockAgent.Setup(a => a.Id).Returns(Guid.NewGuid());
        _actor = new OrleansGAgentActor(_mockGrain.Object, _mockAgent.Object);
    }
    
    [Fact]
    public async Task ActivateAsync_Should_Call_Grain_ActivateAsync()
    {
        // Arrange
        _mockGrain.Setup(g => g.ActivateAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        
        // Act
        await _actor.ActivateAsync();
        
        // Assert - Should call with type information
        _mockGrain.Verify(g => g.ActivateAsync(
            It.IsAny<string>(), 
            It.IsAny<string>()), 
            Times.Once);
    }
    
    [Fact]
    public async Task PublishEventAsync_Should_Serialize_And_Send_To_Grain()
    {
        // Arrange
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(new StringValue { Value = "test" }),
            Direction = EventDirection.Down
        };
        
        byte[]? capturedBytes = null;
        _mockGrain.Setup(g => g.HandleEventAsync(It.IsAny<byte[]>()))
            .Callback<byte[]>(bytes => capturedBytes = bytes)
            .Returns(Task.CompletedTask);
        
        // Act
        await _actor.PublishEventAsync(envelope);
        
        // Assert
        Assert.NotNull(capturedBytes);
        
        // Deserialize and verify
        var deserializedEnvelope = EventEnvelope.Parser.ParseFrom(capturedBytes);
        Assert.Equal(envelope.Id, deserializedEnvelope.Id);
        Assert.Equal(envelope.Direction, deserializedEnvelope.Direction);
    }
    
    [Fact]
    public async Task PublishEventAsync_Should_Wrap_Non_EventEnvelope_Messages()
    {
        // Arrange
        var testMessage = new StringValue { Value = "test_message" };
        
        byte[]? capturedBytes = null;
        _mockGrain.Setup(g => g.HandleEventAsync(It.IsAny<byte[]>()))
            .Callback<byte[]>(bytes => capturedBytes = bytes)
            .Returns(Task.CompletedTask);
        
        // Act
        await _actor.PublishEventAsync(testMessage);
        
        // Assert
        Assert.NotNull(capturedBytes);
        
        // Deserialize and verify it was wrapped
        var envelope = EventEnvelope.Parser.ParseFrom(capturedBytes);
        Assert.NotNull(envelope.Id);
        Assert.Equal(EventDirection.Down, envelope.Direction);
        
        // Verify payload
        var unpacked = envelope.Payload.Unpack<StringValue>();
        Assert.Equal("test_message", unpacked.Value);
    }
    
    [Fact]
    public void Id_Should_Return_Agent_Id()
    {
        // Arrange
        var expectedId = Guid.NewGuid();
        _mockAgent.Setup(a => a.Id).Returns(expectedId);
        var actor = new OrleansGAgentActor(_mockGrain.Object, _mockAgent.Object);
        
        // Act
        var actualId = actor.Id;
        
        // Assert
        Assert.Equal(expectedId, actualId);
    }
    
    [Fact]
    public async Task AddChildAsync_Should_Call_Grain_AddChildAsync()
    {
        // Arrange
        var childId = Guid.NewGuid();
        _mockGrain.Setup(g => g.AddChildAsync(childId))
            .Returns(Task.CompletedTask);
        
        // Act
        await _actor.AddChildAsync(childId);
        
        // Assert
        _mockGrain.Verify(g => g.AddChildAsync(childId), Times.Once);
    }
    
    [Fact]
    public async Task RemoveChildAsync_Should_Call_Grain_RemoveChildAsync()
    {
        // Arrange
        var childId = Guid.NewGuid();
        _mockGrain.Setup(g => g.RemoveChildAsync(childId))
            .Returns(Task.CompletedTask);
        
        // Act
        await _actor.RemoveChildAsync(childId);
        
        // Assert
        _mockGrain.Verify(g => g.RemoveChildAsync(childId), Times.Once);
    }
    
    [Fact]
    public async Task GetChildrenAsync_Should_Return_Grain_Children()
    {
        // Arrange
        var expectedChildren = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        _mockGrain.Setup(g => g.GetChildrenAsync())
            .ReturnsAsync(expectedChildren);
        
        // Act
        var actualChildren = await _actor.GetChildrenAsync();
        
        // Assert
        Assert.Equal(expectedChildren, actualChildren);
    }
    
    [Fact]
    public async Task SetParentAsync_Should_Call_Grain_SetParentAsync()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        _mockGrain.Setup(g => g.SetParentAsync(parentId))
            .Returns(Task.CompletedTask);
        
        // Act
        await _actor.SetParentAsync(parentId);
        
        // Assert
        _mockGrain.Verify(g => g.SetParentAsync(parentId), Times.Once);
    }
    
    [Fact]
    public async Task GetParentAsync_Should_Return_Grain_Parent()
    {
        // Arrange
        var expectedParent = Guid.NewGuid();
        _mockGrain.Setup(g => g.GetParentAsync())
            .ReturnsAsync(expectedParent);
        
        // Act
        var actualParent = await _actor.GetParentAsync();
        
        // Assert
        Assert.Equal(expectedParent, actualParent);
    }
    
    [Fact]
    public async Task DeactivateAsync_Should_Call_Grain_DeactivateAsync()
    {
        // Arrange
        _mockGrain.Setup(g => g.DeactivateAsync())
            .Returns(Task.CompletedTask);
        
        // Act
        await _actor.DeactivateAsync();
        
        // Assert
        _mockGrain.Verify(g => g.DeactivateAsync(), Times.Once);
    }
    
    [Fact]
    public async Task PublishEventAsync_Should_Handle_Null_Message()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _actor.PublishEventAsync<IMessage>(null!));
    }
    
    [Fact]
    public async Task PublishEventAsync_Should_Handle_Grain_Errors()
    {
        // Arrange
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(new StringValue { Value = "test" })
        };
        
        _mockGrain.Setup(g => g.HandleEventAsync(It.IsAny<byte[]>()))
            .ThrowsAsync(new InvalidOperationException("Test error"));
        
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _actor.PublishEventAsync(envelope));
    }
}
