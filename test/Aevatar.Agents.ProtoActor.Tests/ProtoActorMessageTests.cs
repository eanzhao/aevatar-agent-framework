using Aevatar.Agents.Abstractions;
using Google.Protobuf.WellKnownTypes;
using Moq;

namespace Aevatar.Agents.ProtoActor.Tests;

public class ProtoActorMessageTests
{
    private readonly Mock<IMessageSerializer> _mockSerializer;

    public ProtoActorMessageTests()
    {
        _mockSerializer = new Mock<IMessageSerializer>();
    }

    [Fact]
    public void Create_SerializesMessage()
    {
        // Arrange
        var message = new StringValue { Value = "Test message" };
        var serializedData = new byte[] { 1, 2, 3 };

        _mockSerializer
            .Setup(s => s.Serialize(message))
            .Returns(serializedData);

        // Act
        var protoMessage = ProtoActorMessage.Create(message, _mockSerializer.Object);

        // Assert
        Assert.NotNull(protoMessage);
        Assert.Equal(serializedData, protoMessage.SerializedMessage);
        Assert.Equal(typeof(StringValue), protoMessage.MessageType);

        _mockSerializer.Verify(s => s.Serialize(message), Times.Once);
    }

    [Fact]
    public void GetMessage_DeserializesMessage()
    {
        // Arrange
        var message = new StringValue { Value = "Test message" };
        var serializedData = new byte[] { 1, 2, 3 };

        _mockSerializer
            .Setup(s => s.Deserialize<StringValue>(serializedData))
            .Returns(message);

        var protoMessage = new ProtoActorMessage(serializedData, typeof(StringValue));

        // Act
        var deserializedMessage = protoMessage.GetMessage(_mockSerializer.Object);

        // Assert
        Assert.NotNull(deserializedMessage);
        Assert.IsType<StringValue>(deserializedMessage);
        var stringValue = (StringValue)deserializedMessage;
        Assert.Equal("Test message", stringValue.Value);
    }

    [Fact]
    public void GetMessage_ThrowsWhenDeserializeMethodNotFound()
    {
        // Arrange
        var serializedData = new byte[] { 1, 2, 3 };

        // Use a non-IMessageSerializer object to force an error
        var badSerializer = new Mock<IMessageSerializer>();

        // Make reflection return null to simulate method not found
        var protoMessage =
            new ProtoActorMessage(serializedData, typeof(int)); // Using a type that doesn't implement IMessage

        // Act & Assert
        // The method will throw ArgumentException due to generic constraints
        var exception = Assert.ThrowsAny<Exception>(() =>
            protoMessage.GetMessage(badSerializer.Object));

        Assert.Contains("violates the constraint", exception.Message);
    }
}