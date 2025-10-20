using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Serialization;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Core.Tests;

public class SerializationTests
{
    private readonly IMessageSerializer _serializer;

    public SerializationTests()
    {
        _serializer = new ProtobufSerializer();
    }

    [Fact]
    public void Serialize_MessageEnvelope_Success()
    {
        // Arrange
        var message = new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Payload = Any.Pack(new StringValue { Value = "Test message" })
        };

        // Act
        var serialized = _serializer.Serialize(message);
        var deserialized = _serializer.Deserialize<MessageEnvelope>(serialized);

        // Assert
        Assert.NotNull(serialized);
        Assert.NotEmpty(serialized);
        Assert.Equal(message.Id, deserialized.Id);
        Assert.Equal(message.Timestamp, deserialized.Timestamp);
            
        var unpackedValue = deserialized.Payload.Unpack<StringValue>();
        Assert.Equal("Test message", unpackedValue.Value);
    }

    [Fact]
    public void Serialize_EventEnvelope_Success()
    {
        // Arrange
        var evt = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Version = 1,
            Payload = Any.Pack(new SubAgentAdded { SubAgentId = Guid.NewGuid().ToString() })
        };

        // Act
        var serialized = _serializer.Serialize(evt);
        var deserialized = _serializer.Deserialize<EventEnvelope>(serialized);

        // Assert
        Assert.NotNull(serialized);
        Assert.NotEmpty(serialized);
        Assert.Equal(evt.Id, deserialized.Id);
        Assert.Equal(evt.Timestamp, deserialized.Timestamp);
        Assert.Equal(evt.Version, deserialized.Version);
            
        var unpackedValue = deserialized.Payload.Unpack<SubAgentAdded>();
        Assert.Equal(
            evt.Payload.Unpack<SubAgentAdded>().SubAgentId, 
            unpackedValue.SubAgentId
        );
    }

    [Fact]
    public void Serialize_ComplexMessage_Success()
    {
        // Arrange
        var llmAgentState = new LLMAgentState
        {
            CurrentVersion = 5,
            LlmConfig = "OpenAI config string"
        };
        llmAgentState.SubAgentIds.Add(Guid.NewGuid().ToString());
        llmAgentState.SubAgentIds.Add(Guid.NewGuid().ToString());

        // Act
        var serialized = _serializer.Serialize(llmAgentState);
        var deserialized = _serializer.Deserialize<LLMAgentState>(serialized);

        // Assert
        Assert.NotNull(serialized);
        Assert.NotEmpty(serialized);
        Assert.Equal(llmAgentState.CurrentVersion, deserialized.CurrentVersion);
        Assert.Equal(llmAgentState.LlmConfig, deserialized.LlmConfig);
        Assert.Equal(llmAgentState.SubAgentIds.Count, deserialized.SubAgentIds.Count);
            
        for (int i = 0; i < llmAgentState.SubAgentIds.Count; i++)
        {
            Assert.Equal(llmAgentState.SubAgentIds[i], deserialized.SubAgentIds[i]);
        }
    }

    [Fact]
    public void Deserialize_InvalidData_ThrowsException()
    {
        // Arrange
        byte[] invalidData = [1, 2, 3, 4];

        // Act & Assert
        // Protobuf deserialization throws TargetInvocationException wrapping the real exception
        Assert.ThrowsAny<Exception>(() => 
            _serializer.Deserialize<MessageEnvelope>(invalidData)
        );
    }
}