using System;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.Core.Tests.Messages;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Xunit;

namespace Aevatar.Agents.Core.Tests.Extensions;

public class MessageExtensionsTests
{
    [Fact(DisplayName = "HasPayload should return true when envelope contains matching payload type")]
    public void HasPayload_ShouldReturnTrueWhenEnvelopeContainsMatchingPayloadType()
    {
        // Arrange
        var testEvent = new TestEvent 
        {
            EventId = "test-123",
            EventData = "Test data",
            Timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow)
        };
        
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(testEvent)
        };
        
        // Act
        var hasPayload = envelope.HasPayload<TestEvent>();
        
        // Assert
        hasPayload.Should().BeTrue();
    }
    
    [Fact(DisplayName = "HasPayload should return false when envelope contains different payload type")]
    public void HasPayload_ShouldReturnFalseWhenEnvelopeContainsDifferentPayloadType()
    {
        // Arrange
        var testEvent = new TestEvent
        {
            EventId = "test-123",
            EventData = "Test data"
        };
        
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(testEvent)
        };
        
        // Act
        var hasPayload = envelope.HasPayload<TestCommand>();
        
        // Assert
        hasPayload.Should().BeFalse();
    }
    
    [Fact(DisplayName = "HasPayload should return false when envelope has null payload")]
    public void HasPayload_ShouldReturnFalseWhenEnvelopeHasNullPayload()
    {
        // Arrange
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = null
        };
        
        // Act
        var hasPayload = envelope.HasPayload<TestEvent>();
        
        // Assert
        hasPayload.Should().BeFalse();
    }
    
    [Fact(DisplayName = "UnpackPayload should return correct payload when type matches")]
    public void UnpackPayload_ShouldReturnCorrectPayloadWhenTypeMatches()
    {
        // Arrange
        var testEvent = new TestEvent 
        {
            EventId = "test-123",
            EventData = "Test data",
            Timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow)
        };
        
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(testEvent)
        };
        
        // Act
        var unpackedEvent = envelope.UnpackPayload<TestEvent>();
        
        // Assert
        unpackedEvent.Should().NotBeNull();
        unpackedEvent!.EventId.Should().Be(testEvent.EventId);
        unpackedEvent.EventData.Should().Be(testEvent.EventData);
        unpackedEvent.Timestamp.Should().Be(testEvent.Timestamp);
    }
    
    [Fact(DisplayName = "UnpackPayload should return null when type doesn't match")]
    public void UnpackPayload_ShouldReturnNullWhenTypeDoesNotMatch()
    {
        // Arrange
        var testEvent = new TestEvent
        {
            EventId = "test-123",
            EventData = "Test data"
        };
        
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(testEvent)
        };
        
        // Act
        var unpackedCommand = envelope.UnpackPayload<TestCommand>();
        
        // Assert
        unpackedCommand.Should().BeNull();
    }
    
    [Fact(DisplayName = "UnpackPayload should return null when payload is null")]
    public void UnpackPayload_ShouldReturnNullWhenPayloadIsNull()
    {
        // Arrange
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = null
        };
        
        // Act
        var unpackedEvent = envelope.UnpackPayload<TestEvent>();
        
        // Assert
        unpackedEvent.Should().BeNull();
    }
    
    [Fact(DisplayName = "CreateEventEnvelope should create envelope with correct payload and version")]
    public void CreateEventEnvelope_ShouldCreateEnvelopeWithCorrectPayloadAndVersion()
    {
        // Arrange
        var testEvent = new TestEvent 
        {
            EventId = "test-123",
            EventData = "Test data",
            Timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow)
        };
        const long version = 42;
        
        // Act
        var envelope = testEvent.CreateEventEnvelope(version);
        
        // Assert
        envelope.Should().NotBeNull();
        envelope.Id.Should().NotBeNullOrWhiteSpace();
        envelope.Version.Should().Be(version);
        envelope.Timestamp.Should().BeGreaterThan(0);
        envelope.Payload.Should().NotBeNull();
        
        // Verify the payload can be unpacked correctly
        var unpackedEvent = envelope.UnpackPayload<TestEvent>();
        unpackedEvent.Should().NotBeNull();
        unpackedEvent!.EventId.Should().Be(testEvent.EventId);
        unpackedEvent.EventData.Should().Be(testEvent.EventData);
    }
    
    [Fact(DisplayName = "CreateEventEnvelope should generate unique IDs for different envelopes")]
    public void CreateEventEnvelope_ShouldGenerateUniqueIdsForDifferentEnvelopes()
    {
        // Arrange
        var testEvent = new TestEvent
        {
            EventId = "test-123",
            EventData = "Test data"
        };
        
        // Act
        var envelope1 = testEvent.CreateEventEnvelope(1);
        var envelope2 = testEvent.CreateEventEnvelope(2);
        
        // Assert
        envelope1.Id.Should().NotBe(envelope2.Id);
        envelope1.Version.Should().Be(1);
        envelope2.Version.Should().Be(2);
    }
    
    [Fact(DisplayName = "CreateEventEnvelope should set timestamp close to current time")]
    public void CreateEventEnvelope_ShouldSetTimestampCloseToCurrentTime()
    {
        // Arrange
        var testEvent = new TestEvent
        {
            EventId = "test-123",
            EventData = "Test data"
        };
        var beforeTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        
        // Act
        var envelope = testEvent.CreateEventEnvelope(1);
        var afterTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        
        // Assert
        envelope.Timestamp.Should().BeGreaterThanOrEqualTo(beforeTime);
        envelope.Timestamp.Should().BeLessThanOrEqualTo(afterTime);
    }
    
    [Fact(DisplayName = "Extension methods should work with complex message types")]
    public void ExtensionMethods_ShouldWorkWithComplexMessageTypes()
    {
        // Arrange
        var complexEvent = new TestStateChangeEvent
        {
            OldState = "State1",
            NewState = "State2"
        };
        
        // Act
        var envelope = complexEvent.CreateEventEnvelope(10);
        var hasCorrectPayload = envelope.HasPayload<TestStateChangeEvent>();
        var hasWrongPayload = envelope.HasPayload<TestEvent>();
        var unpacked = envelope.UnpackPayload<TestStateChangeEvent>();
        
        // Assert
        hasCorrectPayload.Should().BeTrue();
        hasWrongPayload.Should().BeFalse();
        unpacked.Should().NotBeNull();
        unpacked!.OldState.Should().Be("State1");
        unpacked.NewState.Should().Be("State2");
    }
    
    [Fact(DisplayName = "Extension methods should handle GeneralConfigEvent correctly")]
    public void ExtensionMethods_ShouldHandleGeneralConfigEventCorrectly()
    {
        // Arrange
        var configEvent = new GeneralConfigEvent
        {
            ConfigKey = "test.setting",
            ConfigValue = "enabled"
        };
        
        // Act
        var envelope = configEvent.CreateEventEnvelope(1);
        var hasPayload = envelope.HasPayload<GeneralConfigEvent>();
        var unpacked = envelope.UnpackPayload<GeneralConfigEvent>();
        
        // Assert
        hasPayload.Should().BeTrue();
        unpacked.Should().NotBeNull();
        unpacked!.ConfigKey.Should().Be("test.setting");
        unpacked.ConfigValue.Should().Be("enabled");
    }
}
