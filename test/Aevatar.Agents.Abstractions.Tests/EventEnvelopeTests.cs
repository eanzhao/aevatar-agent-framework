using Aevatar.Agents.Abstractions.Helpers;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Abstractions.Tests;

/// <summary>
/// EventEnvelope unit tests
/// Test event envelope creation, serialization, propagation control and timestamp generation
/// </summary>
public class EventEnvelopeTests
{
    #region Creation and Serialization Tests

    [Fact(DisplayName = "EventEnvelope should create and serialize correctly")]
    public void Should_Create_And_Serialize_Correctly()
    {
        // Arrange
        var eventId = Guid.NewGuid().ToString();
        var publisherId = Guid.NewGuid().ToString();
        var correlationId = Guid.NewGuid().ToString();
        var eventData = new StringValue { Value = "Test data" };
        var timestamp = TimestampHelper.GetUtcNow();

        // Act
        var envelope = new EventEnvelope
        {
            Id = eventId,
            PublisherId = publisherId,
            CorrelationId = correlationId,
            Timestamp = timestamp,
            Version = 1,
            Payload = Any.Pack(eventData),
            Direction = EventDirection.Up,
            Message = "Test event message"
        };

        // Assert
        envelope.ShouldNotBeNull();
        envelope.Id.ShouldBe(eventId);
        envelope.PublisherId.ShouldBe(publisherId);
        envelope.CorrelationId.ShouldBe(correlationId);
        envelope.Timestamp.ShouldBe(timestamp);
        envelope.Version.ShouldBe(1);
        envelope.Payload.ShouldNotBeNull();
        envelope.Direction.ShouldBe(EventDirection.Up);
        envelope.Message.ShouldBe("Test event message");

        // Test serialization
        var serialized = envelope.ToByteArray();
        serialized.ShouldNotBeNull();
        serialized.Length.ShouldBeGreaterThan(0);

        // Test deserialization
        var deserialized = EventEnvelope.Parser.ParseFrom(serialized);
        deserialized.ShouldNotBeNull();
        deserialized.Id.ShouldBe(eventId);
        deserialized.PublisherId.ShouldBe(publisherId);
        deserialized.CorrelationId.ShouldBe(correlationId);
        deserialized.Timestamp.ShouldBe(timestamp);
        deserialized.Version.ShouldBe(1);
    }

    [Fact(DisplayName = "EventEnvelope should serialize complex payload correctly")]
    public void Should_Serialize_Complex_Payload()
    {
        // Arrange
        var complexData = new Duration { Seconds = 100, Nanos = 500 };
        var envelope = new EventEnvelope
        {
            Id = "test-123",
            PublisherId = "publisher-456",
            Version = 2,
            Timestamp = TimestampHelper.GetUtcNow(),
            Payload = Any.Pack(complexData)
        };

        // Act
        var serialized = envelope.ToByteArray();
        var deserialized = EventEnvelope.Parser.ParseFrom(serialized);

        // Assert
        deserialized.Payload.ShouldNotBeNull();
        deserialized.Payload.Is(Duration.Descriptor).ShouldBeTrue();

        var unpackedData = deserialized.Payload.Unpack<Duration>();
        unpackedData.ShouldNotBeNull();
        unpackedData.Seconds.ShouldBe(100);
        unpackedData.Nanos.ShouldBe(500);
    }

    [Fact(DisplayName = "EventEnvelope should handle null payload")]
    public void Should_Handle_Null_Payload()
    {
        // Arrange & Act
        var envelope = new EventEnvelope
        {
            Id = "test-null",
            PublisherId = "publisher-null",
            Timestamp = TimestampHelper.GetUtcNow(),
            // Payload is not set (null)
        };

        // Assert
        envelope.Payload.ShouldBeNull();

        // Should be able to serialize
        var serialized = envelope.ToByteArray();
        serialized.ShouldNotBeNull();

        var deserialized = EventEnvelope.Parser.ParseFrom(serialized);
        deserialized.Payload.ShouldBeNull();
    }

    #endregion

    #region Propagation Control Tests

    [Fact(DisplayName = "EventEnvelope should handle propagation control")]
    public void Should_Handle_Propagation_Control()
    {
        // Arrange
        var envelope = new EventEnvelope
        {
            Id = "prop-test",
            PublisherId = "publisher-prop",
            Direction = EventDirection.Both,
            ShouldStopPropagation = false,
            MaxHopCount = 5,
            CurrentHopCount = 0,
            MinHopCount = 1
        };

        // Assert
        envelope.Direction.ShouldBe(EventDirection.Both);
        envelope.ShouldStopPropagation.ShouldBeFalse();
        envelope.MaxHopCount.ShouldBe(5);
        envelope.CurrentHopCount.ShouldBe(0);
        envelope.MinHopCount.ShouldBe(1);

        // Test modifying propagation control
        envelope.CurrentHopCount = 3;
        envelope.ShouldStopPropagation = true;

        envelope.CurrentHopCount.ShouldBe(3);
        envelope.ShouldStopPropagation.ShouldBeTrue();
    }

    [Fact(DisplayName = "EventEnvelope should track publisher chain")]
    public void Should_Track_Publisher_Chain()
    {
        // Arrange
        var envelope = new EventEnvelope
        {
            Id = "chain-test",
            PublisherId = "publisher-1"
        };

        // Act - Add publisher chain
        envelope.Publishers.Add("publisher-1");
        envelope.Publishers.Add("publisher-2");
        envelope.Publishers.Add("publisher-3");

        // Assert
        envelope.Publishers.Count.ShouldBe(3);
        envelope.Publishers[0].ShouldBe("publisher-1");
        envelope.Publishers[1].ShouldBe("publisher-2");
        envelope.Publishers[2].ShouldBe("publisher-3");

        // Test serialization
        var serialized = envelope.ToByteArray();
        var deserialized = EventEnvelope.Parser.ParseFrom(serialized);

        deserialized.Publishers.Count.ShouldBe(3);
        deserialized.Publishers.ShouldContain("publisher-2");
    }

    [Fact(DisplayName = "EventEnvelope should handle event direction")]
    public void Should_Handle_Event_Direction()
    {
        // Arrange & Act & Assert
        var upEvent = new EventEnvelope
        {
            Id = "up-event",
            Direction = EventDirection.Up
        };
        upEvent.Direction.ShouldBe(EventDirection.Up);

        var downEvent = new EventEnvelope
        {
            Id = "down-event",
            Direction = EventDirection.Down
        };
        downEvent.Direction.ShouldBe(EventDirection.Down);

        var bothEvent = new EventEnvelope
        {
            Id = "both-event",
            Direction = EventDirection.Both
        };
        bothEvent.Direction.ShouldBe(EventDirection.Both);

        var unspecifiedEvent = new EventEnvelope
        {
            Id = "unspecified-event"
            // Direction not set
        };
        unspecifiedEvent.Direction.ShouldBe(EventDirection.Unspecified);
    }

    #endregion

    #region Timestamp Tests

    [Fact(DisplayName = "EventEnvelope should generate valid timestamps")]
    public void Should_Generate_Valid_Timestamps()
    {
        // Arrange
        var beforeCreation = TimestampHelper.GetUtcNow();

        // Act
        var envelope = new EventEnvelope
        {
            Id = "timestamp-test",
            PublisherId = "publisher",
            Timestamp = TimestampHelper.GetUtcNow()
        };

        var afterCreation = TimestampHelper.GetUtcNow();

        // Assert
        envelope.Timestamp.Seconds.ShouldBeGreaterThan(0);
        envelope.Timestamp.ShouldBeGreaterThanOrEqualTo(beforeCreation);
        envelope.Timestamp.ShouldBeLessThanOrEqualTo(afterCreation);
    }

    [Fact(DisplayName = "EventEnvelope timestamp should serialize correctly")]
    public void Timestamp_Should_Serialize_Correctly()
    {
        // Arrange
        var specificTime = new DateTimeOffset(2024, 1, 15, 10, 30, 45, TimeSpan.Zero);
        var timestampMillis = Timestamp.FromDateTimeOffset(specificTime);
        var envelope = new EventEnvelope
        {
            Id = "timestamp-serialize",
            PublisherId = "publisher",
            Timestamp = timestampMillis,
        };

        // Act
        var serialized = envelope.ToByteArray();
        var deserialized = EventEnvelope.Parser.ParseFrom(serialized);

        // Assert
        deserialized.Timestamp.ShouldBe(timestampMillis);
    }

    [Fact(DisplayName = "EventEnvelope should handle zero timestamp")]
    public void Should_Handle_Zero_Timestamp()
    {
        // Arrange & Act
        var envelope = new EventEnvelope
        {
            Id = "no-timestamp",
            PublisherId = "publisher"
            // Timestamp not set (default to 0)
        };

        // Assert
        envelope.Timestamp.ShouldBeNull();

        // Should be able to serialize
        var serialized = envelope.ToByteArray();
        var deserialized = EventEnvelope.Parser.ParseFrom(serialized);

        deserialized.Timestamp.ShouldBeNull();
    }

    #endregion

    #region Edge Case Tests

    [Fact(DisplayName = "EventEnvelope should validate required fields")]
    public void Should_Validate_Required_Fields()
    {
        // Arrange & Act
        var envelope = new EventEnvelope();

        // Assert - Default values
        envelope.Id.ShouldBe(string.Empty);
        envelope.PublisherId.ShouldBe(string.Empty);
        envelope.CorrelationId.ShouldBe(string.Empty);
        envelope.Payload.ShouldBeNull();
        envelope.Timestamp.ShouldBeNull();
        envelope.Version.ShouldBe(0);
        envelope.Direction.ShouldBe(EventDirection.Unspecified);
        envelope.ShouldStopPropagation.ShouldBeFalse();
        envelope.MaxHopCount.ShouldBe(0);
        envelope.CurrentHopCount.ShouldBe(0);
        envelope.MinHopCount.ShouldBe(0);
        envelope.Message.ShouldBe(string.Empty);
        envelope.Publishers.ShouldNotBeNull();
        envelope.Publishers.Count.ShouldBe(0);
    }

    [Fact(DisplayName = "EventEnvelope should handle special characters in fields")]
    public void Should_Handle_Special_Characters()
    {
        // Arrange
        var envelope = new EventEnvelope
        {
            Id = "测试-123-テスト",
            PublisherId = "publisher@example.com",
            CorrelationId = "关联-相関-correlation",
            Message = "消息-メッセージ-message 🚀✨💡"
        };

        envelope.Publishers.Add("发布者-パブリッシャー");
        envelope.Publishers.Add("publisher-2-🎯");

        // Act
        var serialized = envelope.ToByteArray();
        var deserialized = EventEnvelope.Parser.ParseFrom(serialized);

        // Assert
        deserialized.Id.ShouldBe("测试-123-テスト");
        deserialized.PublisherId.ShouldBe("publisher@example.com");
        deserialized.CorrelationId.ShouldBe("关联-相関-correlation");
        deserialized.Message.ShouldBe("消息-メッセージ-message 🚀✨💡");
        deserialized.Publishers[0].ShouldBe("发布者-パブリッシャー");
        deserialized.Publishers[1].ShouldBe("publisher-2-🎯");
    }

    [Fact(DisplayName = "EventEnvelope should handle large payload")]
    public void Should_Handle_Large_Payload()
    {
        // Arrange
        var largeString = new string('X', 10000); // 10KB string
        var largeData = new StringValue { Value = largeString };

        var envelope = new EventEnvelope
        {
            Id = "large-payload",
            PublisherId = "publisher",
            Timestamp = TimestampHelper.GetUtcNow(),
            Payload = Any.Pack(largeData)
        };

        // Act
        var serialized = envelope.ToByteArray();
        var deserialized = EventEnvelope.Parser.ParseFrom(serialized);

        // Assert
        var unpackedData = deserialized.Payload.Unpack<StringValue>();
        unpackedData.Value.Length.ShouldBe(10000);
        unpackedData.Value.ShouldBe(largeString);
    }

    #endregion

    #region Utility Method Tests

    [Fact(DisplayName = "EventEnvelope should support cloning")]
    public void Should_Support_Cloning()
    {
        // Arrange
        var original = new EventEnvelope
        {
            Id = "original",
            PublisherId = "publisher",
            CorrelationId = "correlation-123",
            Timestamp = TimestampHelper.GetUtcNow(),
            Version = 1,
            Direction = EventDirection.Up,
            Payload = Any.Pack(new StringValue { Value = "test" })
        };

        original.Publishers.Add("publisher-1");
        original.Publishers.Add("publisher-2");

        // Act
        var clone = original.Clone();

        // Assert
        clone.ShouldNotBeNull();
        clone.ShouldNotBeSameAs(original);
        clone.Id.ShouldBe(original.Id);
        clone.PublisherId.ShouldBe(original.PublisherId);
        clone.CorrelationId.ShouldBe(original.CorrelationId);
        clone.Version.ShouldBe(original.Version);
        clone.Direction.ShouldBe(original.Direction);
        clone.Publishers.Count.ShouldBe(2);

        // Modifying clone should not affect original object
        clone.Id = "modified";
        clone.Publishers.Add("publisher-3");

        original.Id.ShouldBe("original");
        original.Publishers.Count.ShouldBe(2);
    }

    [Fact(DisplayName = "EventEnvelope should implement equality correctly")]
    public void Should_Implement_Equality_Correctly()
    {
        // Arrange
        var envelope1 = new EventEnvelope
        {
            Id = "same-id",
            PublisherId = "publisher",
            Version = 1
        };

        var envelope2 = new EventEnvelope
        {
            Id = "same-id",
            PublisherId = "publisher",
            Version = 1
        };

        var envelope3 = new EventEnvelope
        {
            Id = "different-id",
            PublisherId = "publisher",
            Version = 1
        };

        // Act & Assert
        envelope1.Equals(envelope2).ShouldBeTrue();
        envelope1.Equals(envelope3).ShouldBeFalse();
        envelope1.GetHashCode().ShouldBe(envelope2.GetHashCode());
    }

    #endregion

    #region Version Control Tests

    [Fact(DisplayName = "EventEnvelope should handle version numbers")]
    public void Should_Handle_Version_Numbers()
    {
        // Arrange
        var envelope = new EventEnvelope
        {
            Id = "version-test",
            Version = 42
        };

        // Assert
        envelope.Version.ShouldBe(42);

        // Act - Serialize and deserialize
        var serialized = envelope.ToByteArray();
        var deserialized = EventEnvelope.Parser.ParseFrom(serialized);

        // Assert
        deserialized.Version.ShouldBe(42);
    }

    #endregion

    #region Correlation ID Tests

    [Fact(DisplayName = "EventEnvelope should handle correlation ID")]
    public void Should_Handle_Correlation_Id()
    {
        // Arrange
        var correlationId = "correlation-" + Guid.NewGuid().ToString();
        var envelope = new EventEnvelope
        {
            Id = "test-correlation",
            CorrelationId = correlationId
        };

        // Assert
        envelope.CorrelationId.ShouldBe(correlationId);

        // Multiple events can share the same correlation ID
        var relatedEnvelope = new EventEnvelope
        {
            Id = "related-event",
            CorrelationId = correlationId
        };

        relatedEnvelope.CorrelationId.ShouldBe(envelope.CorrelationId);
    }

    #endregion
}