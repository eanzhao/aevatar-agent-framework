using System;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Tests.Agents;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Core.Tests;

public class ConfigProtectionTests
{
    /// <summary>
    /// Test agent that attempts to modify Config outside of allowed contexts
    /// </summary>
    public class TestConfigProtectedAgent : GAgentBase<TestAgentState, TestAgentConfig>
    {
        // Method that incorrectly tries to modify Config directly
        public void TryModifyConfigDirectly()
        {
            // This should throw an exception due to protection
            Config = new TestAgentConfig { AgentName = "DirectModification" };
        }

        // Method that modifies Config properties (can't be intercepted for Protobuf)
        public void ModifyConfigProperty()
        {
            // This won't throw, but should generate DEBUG warning
            Config.AgentName = "PropertyModification";
        }

        [EventHandler]
        public async Task HandleTestEvent(TestEvent evt)
        {
            // This is allowed - Config can be modified in event handlers
            Config.AgentName = "EventHandlerModification";
            await Task.CompletedTask;
        }

        protected override Task OnActivateAsync(CancellationToken ct = default)
        {
            // This is allowed - Config can be modified in OnActivateAsync
            Config.AgentName = "OnActivateModification";
            Config.MaxRetries = 10;
            return base.OnActivateAsync(ct);
        }

        public override string GetDescription()
        {
            return $"TestConfigProtectedAgent: {Config.AgentName}";
        }
    }

    [Fact(DisplayName = "Config should be protected from direct assignment outside allowed contexts")]
    public async Task Config_Direct_Assignment_Should_Fail_Outside_Allowed_Contexts()
    {
        // Arrange
        var agent = new TestConfigProtectedAgent();

        // Act & Assert - Direct Config assignment should fail
        var exception = Should.Throw<InvalidOperationException>(() => { agent.TryModifyConfigDirectly(); });

        exception.Message.ShouldContain("Direct Config assignment is not allowed outside of event handlers");

        // But OnActivateAsync should work
        await agent.ActivateAsync();
        agent.GetDescription().ShouldContain("OnActivateModification");
    }

    [Fact(DisplayName = "Config should be modifiable in event handlers")]
    public async Task Config_Should_Be_Modifiable_In_EventHandlers()
    {
        // Arrange
        var agent = new TestConfigProtectedAgent();
        await agent.ActivateAsync();

        // Act - Simulate event handler invocation using reflection
        var handler = agent.GetType().GetMethod(nameof(TestConfigProtectedAgent.HandleTestEvent));
        handler.ShouldNotBeNull();

        // Use reflection to invoke the handler with proper context
        var invokeMethod = typeof(GAgentBase).GetMethod(
            "InvokeHandler",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        invokeMethod.ShouldNotBeNull();

        await (Task)invokeMethod.Invoke(
            agent,
            [handler, new TestEvent { EventId = "test-123" }, CancellationToken.None])!;

        // Assert - Config should be modified
        agent.GetConfig().AgentName.ShouldBe("EventHandlerModification");
    }

    [Fact(DisplayName = "Config property modifications cannot be intercepted for Protobuf classes")]
    public void Config_Property_Modification_Cannot_Be_Intercepted()
    {
        // Arrange
        var agent = new TestConfigProtectedAgent();

        // Act - This won't throw because we can't intercept property setters on Protobuf objects
        Should.NotThrow(() => { agent.ModifyConfigProperty(); });

        // Assert - The modification went through
        agent.GetConfig().AgentName.ShouldBe("PropertyModification");

        // Note: In DEBUG mode, this would generate a warning in Debug output
    }
}