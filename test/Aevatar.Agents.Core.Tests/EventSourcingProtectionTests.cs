using Aevatar.Agents.Core.Tests.Agents;
using Aevatar.Agents.Core.Tests.Fixtures;
using Aevatar.Agents.Core.StateProtection;
using Shouldly;

namespace Aevatar.Agents.Core.Tests;

public class EventSourcingProtectionTests(CoreTestFixture fixture) : IClassFixture<CoreTestFixture>
{
    [Fact(DisplayName = "Should throw exception when setting State directly if EventStore is present")]
    public void Should_Throw_Exception_When_Setting_State_Directly_With_EventStore()
    {
        // Arrange
        var agent = new BasicTestAgent();

        // Set _currentVersion to 1 via reflection to simulate active Event Sourcing
        var versionField = typeof(GAgentBase<TestAgentState>).GetField("_currentVersion",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        versionField.ShouldNotBeNull();
        versionField.SetValue(agent, 1L);

        // Act & Assert
        var exception = Should.Throw<System.Reflection.TargetInvocationException>(() =>
        {
            // We need to access the protected State property. 
            var stateProp = typeof(GAgentBase<TestAgentState>).GetProperty("State",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            stateProp.ShouldNotBeNull();

            // We must be in a valid scope to pass the first check (EnsureModifiable),
            // so that we can hit the second check (Version > 0).
            using (StateProtectionContext.BeginInitializationScope())
            {
                stateProp.SetValue(agent, new TestAgentState());
            }
        });

        exception.InnerException.ShouldBeOfType<InvalidOperationException>();
        exception.InnerException.Message.ShouldContain(
            "Direct State modification is not allowed when Event Sourcing is active");
    }

    [Fact(DisplayName = "Should allow setting State directly if EventStore is NOT present")]
    public void Should_Allow_Setting_State_Directly_Without_EventStore()
    {
        // Arrange
        var agent = new BasicTestAgent();
        // Ensure EventStore is null (default)

        // Act & Assert
        Should.NotThrow(() =>
        {
            var stateProp = typeof(GAgentBase<TestAgentState>).GetProperty("State",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            stateProp.ShouldNotBeNull();
            // We need to be in a valid StateProtectionContext to set state, 
            // or the State setter will throw "Direct State assignment" error from EnsureModifiable.
            // So we must wrap this in a scope.

            using (StateProtectionContext.BeginInitializationScope())
            {
                stateProp.SetValue(agent, new TestAgentState());
            }
        });
    }
}
