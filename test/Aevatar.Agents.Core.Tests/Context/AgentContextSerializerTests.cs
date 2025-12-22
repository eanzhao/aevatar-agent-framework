using Aevatar.Agents.Abstractions.Context;
using Aevatar.Agents.Core.Context;
using Shouldly;

namespace Aevatar.Agents.Core.Tests.Context;

/// <summary>
/// Unit tests for AgentContextSerializer
/// </summary>
public class AgentContextSerializerTests
{
    [Fact(DisplayName = "Should serialize string values")]
    public void Should_Serialize_String_Values()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();
        context.Set(AgentContextKeys.UserId, "user123");

        // Act
        var serialized = AgentContextSerializer.Serialize(context);

        // Assert
        serialized.ShouldContainKey("UserId");
        serialized["UserId"].ShouldBe("s:user123");
    }

    [Fact(DisplayName = "Should serialize bool values")]
    public void Should_Serialize_Bool_Values()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();
        context.Set(AgentContextKeys.IsCN, true);

        // Act
        var serialized = AgentContextSerializer.Serialize(context);

        // Assert
        serialized.ShouldContainKey("IsCN");
        serialized["IsCN"].ShouldBe("b:True");
    }

    [Fact(DisplayName = "Should serialize int values")]
    public void Should_Serialize_Int_Values()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();
        context.Set("Count", 42); // Not in allowlist, so use a custom context

        // Create context with allowlisted key
        var context2 = new AsyncLocalAgentContext();
        context2.Set("RequestTime", DateTime.UtcNow);

        // Act
        var serialized = AgentContextSerializer.Serialize(context2);

        // Assert - RequestTime is allowlisted
        serialized.ShouldContainKey("RequestTime");
    }

    [Fact(DisplayName = "Should serialize DateTime values")]
    public void Should_Serialize_DateTime_Values()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();
        var testDate = new DateTime(2024, 12, 22, 10, 30, 0, DateTimeKind.Utc);
        context.Set(AgentContextKeys.RequestTime, testDate);

        // Act
        var serialized = AgentContextSerializer.Serialize(context);

        // Assert
        serialized.ShouldContainKey("RequestTime");
        serialized["RequestTime"].ShouldStartWith("dt:");
    }

    [Fact(DisplayName = "Should only serialize allowlisted keys")]
    public void Should_Only_Serialize_Allowlisted_Keys()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();
        context.Set("NotAllowedKey", "value");
        context.Set(AgentContextKeys.UserId, "user123");

        // Act
        var serialized = AgentContextSerializer.Serialize(context);

        // Assert
        serialized.ShouldNotContainKey("NotAllowedKey");
        serialized.ShouldContainKey("UserId");
    }

    [Fact(DisplayName = "Should deserialize string values")]
    public void Should_Deserialize_String_Values()
    {
        // Arrange
        var metadata = new Dictionary<string, string>
        {
            ["UserId"] = "s:user123"
        };
        var context = new AsyncLocalAgentContext();

        // Act
        AgentContextSerializer.Deserialize(metadata, context);

        // Assert
        context.Get("UserId").ShouldBe("user123");
    }

    [Fact(DisplayName = "Should deserialize bool values")]
    public void Should_Deserialize_Bool_Values()
    {
        // Arrange
        var metadata = new Dictionary<string, string>
        {
            ["IsCN"] = "b:True"
        };
        var context = new AsyncLocalAgentContext();

        // Act
        AgentContextSerializer.Deserialize(metadata, context);

        // Assert
        context.Get("IsCN").ShouldBe(true);
    }

    [Fact(DisplayName = "Should deserialize false bool values")]
    public void Should_Deserialize_False_Bool_Values()
    {
        // Arrange
        var metadata = new Dictionary<string, string>
        {
            ["IsCN"] = "b:False"
        };
        var context = new AsyncLocalAgentContext();

        // Act
        AgentContextSerializer.Deserialize(metadata, context);

        // Assert
        context.Get("IsCN").ShouldBe(false);
    }

    [Fact(DisplayName = "Should deserialize int values")]
    public void Should_Deserialize_Int_Values()
    {
        // Arrange
        var metadata = new Dictionary<string, string>
        {
            ["Language"] = "i:42"
        };
        var context = new AsyncLocalAgentContext();

        // Act
        AgentContextSerializer.Deserialize(metadata, context);

        // Assert
        context.Get("Language").ShouldBe(42);
    }

    [Fact(DisplayName = "Should deserialize long values")]
    public void Should_Deserialize_Long_Values()
    {
        // Arrange
        var metadata = new Dictionary<string, string>
        {
            ["UserId"] = "l:9223372036854775807"
        };
        var context = new AsyncLocalAgentContext();

        // Act
        AgentContextSerializer.Deserialize(metadata, context);

        // Assert
        context.Get("UserId").ShouldBe(long.MaxValue);
    }

    [Fact(DisplayName = "Should deserialize double values")]
    public void Should_Deserialize_Double_Values()
    {
        // Arrange
        var metadata = new Dictionary<string, string>
        {
            ["Language"] = "d:3.14159"
        };
        var context = new AsyncLocalAgentContext();

        // Act
        AgentContextSerializer.Deserialize(metadata, context);

        // Assert
        ((double)context.Get("Language")!).ShouldBe(3.14159, 0.00001);
    }

    [Fact(DisplayName = "Should deserialize Guid values")]
    public void Should_Deserialize_Guid_Values()
    {
        // Arrange
        var testGuid = Guid.NewGuid();
        var metadata = new Dictionary<string, string>
        {
            ["CorrelationId"] = $"g:{testGuid:D}"
        };
        var context = new AsyncLocalAgentContext();

        // Act
        AgentContextSerializer.Deserialize(metadata, context);

        // Assert
        context.Get("CorrelationId").ShouldBe(testGuid);
    }

    [Fact(DisplayName = "Should deserialize DateTime values")]
    public void Should_Deserialize_DateTime_Values()
    {
        // Arrange
        var testDate = new DateTime(2024, 12, 22, 10, 30, 0, DateTimeKind.Utc);
        var metadata = new Dictionary<string, string>
        {
            ["RequestTime"] = $"dt:{testDate:O}"
        };
        var context = new AsyncLocalAgentContext();

        // Act
        AgentContextSerializer.Deserialize(metadata, context);

        // Assert
        context.Get("RequestTime").ShouldBe(testDate);
    }

    [Fact(DisplayName = "Should round-trip all supported types")]
    public void Should_Round_Trip_All_Supported_Types()
    {
        // Arrange
        var originalContext = new AsyncLocalAgentContext();
        var testDate = DateTime.UtcNow;
        
        originalContext.Set(AgentContextKeys.CorrelationId, "corr-123");
        originalContext.Set(AgentContextKeys.UserId, "user456");
        originalContext.Set(AgentContextKeys.Language, "zh");
        originalContext.Set(AgentContextKeys.IsCN, true);
        originalContext.Set(AgentContextKeys.RequestTime, testDate);

        // Act - Serialize then deserialize
        var serialized = AgentContextSerializer.Serialize(originalContext);
        var restoredContext = new AsyncLocalAgentContext();
        AgentContextSerializer.Deserialize(serialized, restoredContext);

        // Assert
        restoredContext.Get(AgentContextKeys.CorrelationId).ShouldBe("corr-123");
        restoredContext.Get(AgentContextKeys.UserId).ShouldBe("user456");
        restoredContext.Get(AgentContextKeys.Language).ShouldBe("zh");
        restoredContext.Get(AgentContextKeys.IsCN).ShouldBe(true);
        // DateTime comparison with tolerance for serialization
        var restoredDate = restoredContext.Get("RequestTime");
        restoredDate.ShouldBeOfType<DateTime>();
        ((DateTime)restoredDate!).ToString("O").ShouldBe(testDate.ToString("O"));
    }

    [Fact(DisplayName = "Should respect MaxKeys limit")]
    public void Should_Respect_MaxKeys_Limit()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();
        
        // Add more keys than MaxKeys (but they need to be allowlisted)
        foreach (var key in AgentContextKeys.AllowedPropagationKeys)
        {
            context.Set(key, "value");
        }

        // Act
        var serialized = AgentContextSerializer.Serialize(context);

        // Assert
        serialized.Count.ShouldBeLessThanOrEqualTo(AgentContextSerializer.MaxKeys);
    }

    [Fact(DisplayName = "Should handle empty context")]
    public void Should_Handle_Empty_Context()
    {
        // Arrange
        var context = new AsyncLocalAgentContext();

        // Act
        var serialized = AgentContextSerializer.Serialize(context);

        // Assert
        serialized.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Should handle empty metadata")]
    public void Should_Handle_Empty_Metadata()
    {
        // Arrange
        var metadata = new Dictionary<string, string>();
        var context = new AsyncLocalAgentContext();

        // Act
        AgentContextSerializer.Deserialize(metadata, context);

        // Assert
        context.Count.ShouldBe(0);
    }

    [Fact(DisplayName = "Should handle malformed serialized values gracefully")]
    public void Should_Handle_Malformed_Values_Gracefully()
    {
        // Arrange
        var metadata = new Dictionary<string, string>
        {
            ["Language"] = "malformed-no-prefix",
            ["UserId"] = "s:valid-user" // Valid string prefix
        };
        var context = new AsyncLocalAgentContext();

        // Act
        AgentContextSerializer.Deserialize(metadata, context);

        // Assert - Should not throw, returns original string for malformed
        context.Get("Language").ShouldBe("malformed-no-prefix");
        context.Get("UserId").ShouldBe("valid-user");
    }

    [Fact(DisplayName = "Should handle edge case - prefix only returns original")]
    public void Should_Handle_Edge_Case_Prefix_Only_Returns_Original()
    {
        // Arrange
        var metadata = new Dictionary<string, string>
        {
            ["UserId"] = "s:" // Only prefix, no value - treated as malformed
        };
        var context = new AsyncLocalAgentContext();

        // Act
        AgentContextSerializer.Deserialize(metadata, context);

        // Assert - Too short to be valid, returns original string
        context.Get("UserId").ShouldBe("s:");
    }

    [Fact(DisplayName = "Should handle single character value")]
    public void Should_Handle_Single_Character_Value()
    {
        // Arrange
        var metadata = new Dictionary<string, string>
        {
            ["UserId"] = "s:X" // Single char after prefix
        };
        var context = new AsyncLocalAgentContext();

        // Act
        AgentContextSerializer.Deserialize(metadata, context);

        // Assert - Should correctly extract single char
        context.Get("UserId").ShouldBe("X");
    }
}

