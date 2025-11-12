using System;
using System.IO;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Aevatar.Agents.Runtime.Orleans;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Moq;
using Orleans.Streams;
using Xunit;

namespace Aevatar.Agents.Orleans.Tests;

/// <summary>
/// Simple unit tests for Orleans Streaming without TestCluster
/// </summary>
public class SimpleOrleansStreamingTests
{
    [Fact]
    public void OrleansMessageStream_Should_Initialize_Correctly()
    {
        // Arrange
        var streamId = Guid.NewGuid();
        var mockStream = new Mock<IAsyncStream<byte[]>>();
        
        // Act
        var messageStream = new OrleansMessageStream(streamId, mockStream.Object);
        
        // Assert
        Assert.NotNull(messageStream);
        Assert.Equal(streamId, messageStream.StreamId);
    }
    
    [Fact]
    public async Task OrleansMessageStream_Should_Produce_EventEnvelope()
    {
        // Arrange
        var streamId = Guid.NewGuid();
        var mockStream = new Mock<IAsyncStream<byte[]>>();
        byte[]? capturedBytes = null;
        
        mockStream.Setup(s => s.OnNextAsync(It.IsAny<byte[]>(), It.IsAny<StreamSequenceToken>()))
            .Callback<byte[], StreamSequenceToken>((bytes, token) => capturedBytes = bytes)
            .Returns(Task.CompletedTask);
        
        var messageStream = new OrleansMessageStream(streamId, mockStream.Object);
        
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(new StringValue { Value = "test message" }),
            Direction = EventDirection.Down
        };
        
        // Act
        await messageStream.ProduceAsync(envelope);
        
        // Assert
        Assert.NotNull(capturedBytes);
        
        // Verify we can deserialize it back
        var deserialized = EventEnvelope.Parser.ParseFrom(capturedBytes);
        Assert.Equal(envelope.Id, deserialized.Id);
        Assert.Equal(envelope.Direction, deserialized.Direction);
    }
    
    [Fact]
    public async Task OrleansMessageStream_Should_Subscribe_And_Handle_Events()
    {
        // Arrange
        var streamId = Guid.NewGuid();
        var mockStream = new Mock<IAsyncStream<byte[]>>();
        var messageStream = new OrleansMessageStream(streamId, mockStream.Object);
        
        EventEnvelope? receivedEnvelope = null;
        var tcs = new TaskCompletionSource<bool>();
        
        // Setup subscription
        mockStream.Setup(s => s.SubscribeAsync(It.IsAny<IAsyncObserver<byte[]>>()))
            .ReturnsAsync((StreamSubscriptionHandle<byte[]>)null!);
        
        // Act
        await messageStream.SubscribeAsync<EventEnvelope>(async envelope =>
        {
            receivedEnvelope = envelope;
            tcs.SetResult(true);
            await Task.CompletedTask;
        });
        
        // Simulate receiving an event
        var testEnvelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(new StringValue { Value = "received message" }),
            Direction = EventDirection.Up
        };
        
        // Get the observer that was registered
        var observerCapture = mockStream.Invocations[0].Arguments[0] as IAsyncObserver<byte[]>;
        Assert.NotNull(observerCapture);
        
        // Send event through the observer
        using var stream = new MemoryStream();
        using var output = new CodedOutputStream(stream);
        testEnvelope.WriteTo(output);
        output.Flush();
        
        await observerCapture.OnNextAsync(stream.ToArray());
        
        // Wait for handler to be called
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(1));
        
        // Assert
        Assert.NotNull(receivedEnvelope);
        Assert.Equal(testEnvelope.Id, receivedEnvelope.Id);
    }
    
    [Fact]
    public async Task OrleansMessageStream_Should_Reject_Non_EventEnvelope_For_Produce()
    {
        // Arrange
        var streamId = Guid.NewGuid();
        var mockStream = new Mock<IAsyncStream<byte[]>>();
        var messageStream = new OrleansMessageStream(streamId, mockStream.Object);
        
        var nonEnvelopeMessage = new StringValue { Value = "not an envelope" };
        
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await messageStream.ProduceAsync(nonEnvelopeMessage);
        });
    }
    
    [Fact]
    public void OrleansMessageStreamProvider_Should_Create_Streams()
    {
        // Arrange
        var mockStreamProvider = new Mock<IStreamProvider>();
        var mockStream = new Mock<IAsyncStream<byte[]>>();
        var agentId = Guid.NewGuid();
        
        mockStreamProvider.Setup(sp => sp.GetStream<byte[]>(It.IsAny<StreamId>()))
            .Returns(mockStream.Object);
        
        var provider = new OrleansMessageStreamProvider(mockStreamProvider.Object);
        
        // Act
        var stream = provider.GetStream(agentId);
        
        // Assert
        Assert.NotNull(stream);
        Assert.Equal(agentId, stream.StreamId);
        
        // Verify the stream provider was called with correct parameters
        mockStreamProvider.Verify(sp => sp.GetStream<byte[]>(
            It.Is<StreamId>(id => id.ToString().Contains(agentId.ToString()))),
            Times.Once);
    }
    
    
    // Test agent for streaming
    public class TestStreamAgent : GAgentBase<TestStreamState>
    {
        public TestStreamAgent(Guid id) : base(id) { }
        
        public override Task<string> GetDescriptionAsync()
        {
            return Task.FromResult($"TestStreamAgent {Id}");
        }
        
        [EventHandler]
        public Task HandleStringValue(StringValue value)
        {
            GetState().LastMessage = value.Value;
            GetState().MessageCount++;
            return Task.CompletedTask;
        }
    }
    
    public class TestStreamState
    {
        public string? LastMessage { get; set; }
        public int MessageCount { get; set; }
    }
}
