using Aevatar.Agents;
using Aevatar.Agents.Abstractions.Context;
using Aevatar.Agents.Core.Context;
using Shouldly;

namespace Aevatar.Agents.Core.Tests.Context;

/// <summary>
/// Unit tests for AgentContextPropagator
/// </summary>
public class AgentContextPropagatorTests
{
    [Fact(DisplayName = "InjectContext should add context to envelope")]
    public void InjectContext_Should_Add_Context_To_Envelope()
    {
        // Arrange
        var accessor = new AsyncLocalAgentContextAccessor();
        var context = new AsyncLocalAgentContext();
        context.Set(AgentContextKeys.UserId, "user123");
        context.Set(AgentContextKeys.Language, "zh");
        accessor.Context = context;

        var propagator = new AgentContextPropagator(accessor);
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString()
        };

        // Act
        propagator.InjectContext(envelope);

        // Assert
        envelope.ContextMetadata.ShouldNotBeEmpty();
        envelope.ContextMetadata.ShouldContainKey("UserId");
        envelope.ContextMetadata.ShouldContainKey("Language");
    }

    [Fact(DisplayName = "InjectContext should not fail when no context")]
    public void InjectContext_Should_Not_Fail_When_No_Context()
    {
        // Arrange
        var accessor = new AsyncLocalAgentContextAccessor();
        accessor.Context = null;

        var propagator = new AgentContextPropagator(accessor);
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString()
        };

        // Act & Assert - Should not throw
        Should.NotThrow(() => propagator.InjectContext(envelope));
        envelope.ContextMetadata.ShouldBeEmpty();
    }

    [Fact(DisplayName = "ExtractContext should return context from envelope")]
    public void ExtractContext_Should_Return_Context_From_Envelope()
    {
        // Arrange
        var accessor = new AsyncLocalAgentContextAccessor();
        var propagator = new AgentContextPropagator(accessor);
        
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString()
        };
        envelope.ContextMetadata["UserId"] = "s:user123";
        envelope.ContextMetadata["Language"] = "s:en";
        envelope.ContextMetadata["IsCN"] = "b:True";

        // Act
        var extractedContext = propagator.ExtractContext(envelope);

        // Assert
        extractedContext.ShouldNotBeNull();
        extractedContext!.Get("UserId").ShouldBe("user123");
        extractedContext.Get("Language").ShouldBe("en");
        extractedContext.Get("IsCN").ShouldBe(true);
    }

    [Fact(DisplayName = "ExtractContext should return null for empty metadata")]
    public void ExtractContext_Should_Return_Null_For_Empty_Metadata()
    {
        // Arrange
        var accessor = new AsyncLocalAgentContextAccessor();
        var propagator = new AgentContextPropagator(accessor);
        
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString()
        };
        // No metadata added

        // Act
        var extractedContext = propagator.ExtractContext(envelope);

        // Assert
        extractedContext.ShouldBeNull();
    }

    [Fact(DisplayName = "ApplyContext should update ambient context")]
    public void ApplyContext_Should_Update_Ambient_Context()
    {
        // Arrange
        var accessor = new AsyncLocalAgentContextAccessor();
        var propagator = new AgentContextPropagator(accessor);
        
        var envelope = new EventEnvelope();
        envelope.ContextMetadata["UserId"] = "s:user123";

        // Act
        propagator.ApplyContext(envelope);

        // Assert
        accessor.Context.ShouldNotBeNull();
        accessor.Context!.Get("UserId").ShouldBe("user123");
    }

    [Fact(DisplayName = "Round trip should preserve context")]
    public void Round_Trip_Should_Preserve_Context()
    {
        // Arrange
        var senderAccessor = new AsyncLocalAgentContextAccessor();
        var senderContext = new AsyncLocalAgentContext();
        senderContext.Set(AgentContextKeys.CorrelationId, "corr-abc");
        senderContext.Set(AgentContextKeys.UserId, "user789");
        senderContext.Set(AgentContextKeys.IsCN, false);
        senderAccessor.Context = senderContext;

        var senderPropagator = new AgentContextPropagator(senderAccessor);

        // Simulate sending - inject into envelope
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString()
        };
        senderPropagator.InjectContext(envelope);

        // Simulate receiving - extract from envelope
        var receiverAccessor = new AsyncLocalAgentContextAccessor();
        var receiverPropagator = new AgentContextPropagator(receiverAccessor);
        var receivedContext = receiverPropagator.ExtractContext(envelope);

        // Assert
        receivedContext.ShouldNotBeNull();
        receivedContext!.Get(AgentContextKeys.CorrelationId).ShouldBe("corr-abc");
        receivedContext.Get(AgentContextKeys.UserId).ShouldBe("user789");
        receivedContext.Get(AgentContextKeys.IsCN).ShouldBe(false);
    }

    [Fact(DisplayName = "Should only propagate allowlisted keys")]
    public void Should_Only_Propagate_Allowlisted_Keys()
    {
        // Arrange
        var accessor = new AsyncLocalAgentContextAccessor();
        var context = new AsyncLocalAgentContext();
        context.Set(AgentContextKeys.UserId, "user123");
        context.Set("CustomKey", "custom-value"); // Not in allowlist
        accessor.Context = context;

        var propagator = new AgentContextPropagator(accessor);
        var envelope = new EventEnvelope();

        // Act
        propagator.InjectContext(envelope);

        // Assert
        envelope.ContextMetadata.ShouldContainKey("UserId");
        envelope.ContextMetadata.ShouldNotContainKey("CustomKey");
    }
}
