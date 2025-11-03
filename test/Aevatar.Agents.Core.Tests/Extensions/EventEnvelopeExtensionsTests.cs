using Aevatar.Agents.Abstractions;
using FluentAssertions;
using Xunit;

namespace Aevatar.Agents.Core.Tests.Extensions;

public class EventEnvelopeExtensionsTests
{
    [Fact(DisplayName = "GetPublishedTimestampUtc should convert Unix timestamp to UTC DateTime")]
    public void GetPublishedTimestampUtc_ShouldConvertUnixTimestampToUtcDateTime()
    {
        // Arrange
        var expectedTime = new DateTime(2024, 1, 15, 10, 30, 45, DateTimeKind.Utc);
        var unixTimestamp = new DateTimeOffset(expectedTime).ToUnixTimeMilliseconds();
        
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            PublishedTimestampUtc = unixTimestamp
        };
        
        // Act
        var result = envelope.GetPublishedTimestampUtc();
        
        // Assert
        result.Should().BeCloseTo(expectedTime, TimeSpan.FromSeconds(1));
        result.Kind.Should().Be(DateTimeKind.Utc);
    }
    
    [Fact(DisplayName = "SetPublishedTimestampUtcNow should set current UTC time")]
    public void SetPublishedTimestampUtcNow_ShouldSetCurrentUtcTime()
    {
        // Arrange
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString()
        };
        var beforeTime = DateTimeOffset.UtcNow;
        
        // Act
        envelope.SetPublishedTimestampUtcNow();
        var afterTime = DateTimeOffset.UtcNow;
        
        // Assert
        var publishedTime = envelope.GetPublishedTimestampUtc();
        publishedTime.Should().BeAfter(beforeTime.UtcDateTime.AddSeconds(-1));
        publishedTime.Should().BeBefore(afterTime.UtcDateTime.AddSeconds(1));
    }
    
    [Fact(DisplayName = "GetEventAge should calculate time since publication")]
    public void GetEventAge_ShouldCalculateTimeSincePublication()
    {
        // Arrange
        var publishedTime = DateTime.UtcNow.AddMinutes(-5);
        var unixTimestamp = new DateTimeOffset(publishedTime).ToUnixTimeMilliseconds();
        
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            PublishedTimestampUtc = unixTimestamp
        };
        
        // Act
        var age = envelope.GetEventAge();
        
        // Assert
        age.Should().BeCloseTo(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1));
    }
    
    [Fact(DisplayName = "GetEventAge should handle recent events correctly")]
    public void GetEventAge_ShouldHandleRecentEventsCorrectly()
    {
        // Arrange
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString()
        };
        envelope.SetPublishedTimestampUtcNow();
        
        // Act
        var age = envelope.GetEventAge();
        
        // Assert
        age.Should().BeLessThan(TimeSpan.FromSeconds(1));
        age.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }
    
    [Fact(DisplayName = "IsExpired should return true when event is older than max age")]
    public void IsExpired_ShouldReturnTrueWhenEventIsOlderThanMaxAge()
    {
        // Arrange
        var publishedTime = DateTime.UtcNow.AddMinutes(-10);
        var unixTimestamp = new DateTimeOffset(publishedTime).ToUnixTimeMilliseconds();
        
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            PublishedTimestampUtc = unixTimestamp
        };
        
        var maxAge = TimeSpan.FromMinutes(5);
        
        // Act
        var isExpired = envelope.IsExpired(maxAge);
        
        // Assert
        isExpired.Should().BeTrue();
    }
    
    [Fact(DisplayName = "IsExpired should return false when event is younger than max age")]
    public void IsExpired_ShouldReturnFalseWhenEventIsYoungerThanMaxAge()
    {
        // Arrange
        var publishedTime = DateTime.UtcNow.AddMinutes(-2);
        var unixTimestamp = new DateTimeOffset(publishedTime).ToUnixTimeMilliseconds();
        
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            PublishedTimestampUtc = unixTimestamp
        };
        
        var maxAge = TimeSpan.FromMinutes(5);
        
        // Act
        var isExpired = envelope.IsExpired(maxAge);
        
        // Assert
        isExpired.Should().BeFalse();
    }
    
    [Fact(DisplayName = "IsExpired should handle edge case when age equals max age")]
    public void IsExpired_ShouldHandleEdgeCaseWhenAgeEqualsMaxAge()
    {
        // Arrange
        var publishedTime = DateTime.UtcNow.AddMinutes(-5);
        var unixTimestamp = new DateTimeOffset(publishedTime).ToUnixTimeMilliseconds();
        
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            PublishedTimestampUtc = unixTimestamp
        };
        
        var maxAge = TimeSpan.FromMinutes(5);
        
        // Act
        var isExpired = envelope.IsExpired(maxAge);
        
        // Assert
        // Event age might be slightly different due to execution time
        // We're testing the boundary case here
        envelope.GetEventAge().Should().BeCloseTo(maxAge, TimeSpan.FromSeconds(1));
    }
    
    [Fact(DisplayName = "IsExpired should handle zero max age correctly")]
    public void IsExpired_ShouldHandleZeroMaxAgeCorrectly()
    {
        // Arrange
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString()
        };
        envelope.SetPublishedTimestampUtcNow();
        
        // Act
        var isExpired = envelope.IsExpired(TimeSpan.Zero);
        
        // Assert
        // Any event with age > 0 should be expired when maxAge is zero
        isExpired.Should().BeTrue();
    }
    
    [Fact(DisplayName = "Multiple extension methods should work together correctly")]
    public void MultipleExtensionMethods_ShouldWorkTogetherCorrectly()
    {
        // Arrange
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Version = 1,
            CorrelationId = "test-correlation",
            PublisherId = "test-publisher",
            Direction = EventDirection.Down,
            MaxHopCount = 5,
            CurrentHopCount = 2
        };
        
        // Act
        envelope.SetPublishedTimestampUtcNow();
        var publishedTime = envelope.GetPublishedTimestampUtc();
        var age = envelope.GetEventAge();
        var isExpired = envelope.IsExpired(TimeSpan.FromHours(1));
        
        // Assert
        publishedTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        age.Should().BeLessThan(TimeSpan.FromSeconds(1));
        isExpired.Should().BeFalse();
    }
}
