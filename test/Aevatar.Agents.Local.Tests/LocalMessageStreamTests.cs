using Aevatar.Agents.Abstractions;
using Google.Protobuf.WellKnownTypes;
using Moq;

namespace Aevatar.Agents.Local.Tests;

public class LocalMessageStreamTests
{
    private readonly Mock<IMessageSerializer> _mockSerializer;
    private readonly Guid _streamId;

    public LocalMessageStreamTests()
    {
        _mockSerializer = new Mock<IMessageSerializer>();
        _streamId = Guid.NewGuid();
    }

    [Fact]
    public void Constructor_SetsStreamId()
    {
        // Arrange & Act
        var stream = new LocalMessageStream(_mockSerializer.Object, _streamId);

        // Assert
        Assert.Equal(_streamId, stream.StreamId);
    }

    [Fact]
    public async Task ProduceAsync_SerializesAndWritesToChannel()
    {
        // Arrange
        var message = new StringValue { Value = "Test message" };
        var serializedData = new byte[] { 1, 2, 3 };

        _mockSerializer
            .Setup(s => s.Serialize(message))
            .Returns(serializedData);

        var stream = new LocalMessageStream(_mockSerializer.Object, _streamId);

        // Act
        await stream.ProduceAsync(message);

        // Assert
        _mockSerializer.Verify(s => s.Serialize(message), Times.Once);
        // Cannot directly verify channel write, but no exception means success
    }

    [Fact]
    public async Task SubscribeAsync_RegistersHandler()
    {
        // Arrange
        var message = new StringValue { Value = "Test message" };
        var serializedData = new byte[] { 1, 2, 3 };
        var handlerCalled = false;

        _mockSerializer
            .Setup(s => s.Serialize(message))
            .Returns(serializedData);

        _mockSerializer
            .Setup(s => s.Deserialize<StringValue>(serializedData))
            .Returns(message);

        var stream = new LocalMessageStream(_mockSerializer.Object, _streamId);

        // Act
        await stream.SubscribeAsync<StringValue>(async (msg) =>
        {
            handlerCalled = true;
            Assert.Equal(message.Value, msg.Value);
            await Task.CompletedTask;
        });

        await stream.ProduceAsync(message);

        // Give a small delay to allow the async handler to execute
        await Task.Delay(100);

        // Assert
        _mockSerializer.Verify(s => s.Serialize(message), Times.Once);
        _mockSerializer.Verify(s => s.Deserialize<StringValue>(serializedData), Times.Once);
        Assert.True(handlerCalled, "Handler should have been called");
    }

    [Fact]
    public async Task SubscribeAsync_IgnoresMessagesOfDifferentTypes()
    {
        // Arrange
        var stringMessage = new StringValue { Value = "Test string" };
        var intMessage = new Int32Value { Value = 42 };

        var stringData = new byte[] { 1, 2, 3 };
        var intData = new byte[] { 4, 5, 6 };

        var stringHandlerCalled = false;
        var intHandlerCalled = false;

        _mockSerializer
            .Setup(s => s.Serialize(stringMessage))
            .Returns(stringData);

        _mockSerializer
            .Setup(s => s.Serialize(intMessage))
            .Returns(intData);

        // When deserializing the wrong type, throw an exception instead of returning null
        _mockSerializer
            .Setup(s => s.Deserialize<StringValue>(It.IsAny<byte[]>()))
            .Returns((byte[] data) =>
            {
                if (data.SequenceEqual(stringData))
                    return stringMessage;
                throw new InvalidOperationException("Cannot deserialize");
            });

        _mockSerializer
            .Setup(s => s.Deserialize<Int32Value>(It.IsAny<byte[]>()))
            .Returns((byte[] data) =>
            {
                if (data.SequenceEqual(intData))
                    return intMessage;
                throw new InvalidOperationException("Cannot deserialize");
            });

        var stream = new LocalMessageStream(_mockSerializer.Object, _streamId);

        // Act
        await stream.SubscribeAsync<StringValue>(async (msg) =>
        {
            stringHandlerCalled = true;
            await Task.CompletedTask;
        });

        await stream.SubscribeAsync<Int32Value>(async (msg) =>
        {
            intHandlerCalled = true;
            await Task.CompletedTask;
        });

        // Send string message
        await stream.ProduceAsync(stringMessage);

        // Give time for handlers to execute
        await Task.Delay(150);

        // Assert
        Assert.True(stringHandlerCalled, "String handler should be called");
        Assert.False(intHandlerCalled, "Int handler should not be called for string message");

        // Reset flags
        stringHandlerCalled = false;
        intHandlerCalled = false;

        // Send int message
        await stream.ProduceAsync(intMessage);

        // Give time for handlers to execute
        await Task.Delay(150);

        // Assert
        Assert.False(stringHandlerCalled, "String handler should not be called for int message");
        Assert.True(intHandlerCalled, "Int handler should be called");
    }

    [Fact]
    public async Task SubscribeAsync_HandlesDeserializationErrors()
    {
        // Arrange
        var message = new StringValue { Value = "Test message" };
        var serializedData = new byte[] { 1, 2, 3 };
        var handlerCalled = false;

        _mockSerializer
            .Setup(s => s.Serialize(message))
            .Returns(serializedData);

        _mockSerializer
            .Setup(s => s.Deserialize<StringValue>(serializedData))
            .Throws(new InvalidOperationException("Deserialization error"));

        var stream = new LocalMessageStream(_mockSerializer.Object, _streamId);

        // Act
        await stream.SubscribeAsync<StringValue>(async (msg) =>
        {
            handlerCalled = true;
            await Task.CompletedTask;
        });

        await stream.ProduceAsync(message);

        // Give time for handlers to execute
        await Task.Delay(100);

        // Assert
        _mockSerializer.Verify(s => s.Serialize(message), Times.Once);
        _mockSerializer.Verify(s => s.Deserialize<StringValue>(serializedData), Times.Once);
        Assert.False(handlerCalled, "Handler should not have been called due to exception");
    }
}