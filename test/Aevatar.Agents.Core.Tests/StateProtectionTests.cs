using Xunit;
using FluentAssertions;
using System;
using System.Threading.Tasks;
using Aevatar.Agents.Core.StateProtection;
using Aevatar.Agents.Core.Tests.Agents;
using Aevatar.Agents.Abstractions.Attributes;

namespace Aevatar.Agents.Core.Tests;

/// <summary>
/// Tests for State protection mechanism that ensures State can only be modified in event handlers
/// </summary>
public class StateProtectionTests
{
    [Fact]
    public async Task State_Assignment_Should_Fail_Outside_EventHandler()
    {
        // Arrange
        var agent = new TestProtectedAgent();

        // Act & Assert - Direct state assignment should throw
        var act = () => agent.TryAssignStateDirectly(new TestAgentState { Name = "test" });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*State assignment is not allowed outside of event handlers*");
    }

    [Fact]
    public async Task ValidateStateModificationContext_Should_Fail_Outside_EventHandler()
    {
        // Arrange
        var agent = new TestProtectedAgent();

        // Act & Assert - Validation should throw when not in handler
        var act = () => agent.TryValidateContext();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*is not allowed outside of event handlers*");
    }

    [Fact]
    public async Task State_Modification_Should_Succeed_In_EventHandler()
    {
        // Arrange
        var agent = new TestProtectedAgent();
        var testEvent = new TestEvent
        {
            EventId = "test-1",
            Payload = "test-value"
        };

        // Act - Simulate event handler invocation
        await agent.SimulateEventHandler(testEvent);

        // Assert - State should be modified successfully
        agent.GetState().Name.Should().Be("test-value");
    }

    [Fact]
    public async Task State_Modification_Should_Succeed_In_OnActivateAsync()
    {
        // Arrange & Act
        var agent = new TestProtectedAgent();
        await agent.ActivateAsync();

        // Assert - State initialization in OnActivateAsync should succeed
        agent.GetState().Name.Should().Be("Initialized");
    }

    [Fact]
    public void StateProtectionContext_Should_Track_Handler_Scope()
    {
        // Initially not in handler
        StateProtectionContext.IsInEventHandler.Should().BeFalse();

        // Enter handler scope
        using (var scope = StateProtectionContext.BeginEventHandlerScope())
        {
            StateProtectionContext.IsInEventHandler.Should().BeTrue();

            // Nested scope should maintain state
            using (var nestedScope = StateProtectionContext.BeginEventHandlerScope())
            {
                StateProtectionContext.IsInEventHandler.Should().BeTrue();
            }

            // Still in outer scope
            StateProtectionContext.IsInEventHandler.Should().BeTrue();
        }

        // Exited all scopes
        StateProtectionContext.IsInEventHandler.Should().BeFalse();
    }

    [Fact]
    public async Task Parallel_EventHandlers_Should_Have_Independent_Contexts()
    {
        // Arrange
        var results = new bool[10];

        // Act - Run multiple handlers in parallel
        var tasks = new Task[10];
        for (int i = 0; i < 10; i++)
        {
            var index = i;
            tasks[i] = Task.Run(async () =>
            {
                // Initially not in handler
                results[index] = StateProtectionContext.IsInEventHandler;

                await Task.Delay(Random.Shared.Next(10, 50)); // Random delay

                using (var scope = StateProtectionContext.BeginEventHandlerScope())
                {
                    // Should be in handler now
                    results[index] = StateProtectionContext.IsInEventHandler;
                    await Task.Delay(Random.Shared.Next(10, 50));
                }
            });
        }

        await Task.WhenAll(tasks);

        // Assert - All tasks should have seen correct context
        results.Should().AllSatisfy(r => r.Should().BeTrue());
    }

    [Fact]
    public void Initialization_Scope_Should_Allow_State_Modification()
    {
        // Initially not in initialization
        StateProtectionContext.IsInEventHandler.Should().BeFalse();

        // Enter initialization scope
        using (var scope = StateProtectionContext.BeginInitializationScope())
        {
            // Should be allowed (same flag as event handler)
            StateProtectionContext.IsInEventHandler.Should().BeTrue();

            // This would normally be State modification during initialization
            var canModify = StateProtectionContext.IsInEventHandler;
            canModify.Should().BeTrue();
        }

        // Exited initialization scope
        StateProtectionContext.IsInEventHandler.Should().BeFalse();
    }
}

/// <summary>
/// Test agent for State protection verification
/// </summary>
public class TestProtectedAgent : GAgentBase<TestAgentState>
{
    public TestProtectedAgent() : base()
    {
        // Constructor should not modify State
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        // This should succeed because ActivateAsync wraps this in InitializationScope
        State.Name = "Initialized";
    }

    /// <summary>
    /// Attempts to assign State directly (outside event handler)
    /// This should throw an exception
    /// </summary>
    public void TryAssignStateDirectly(TestAgentState newState)
    {
        State = newState; // This should throw!
    }

    /// <summary>
    /// Attempts to validate modification context
    /// </summary>
    public void TryValidateContext()
    {
        ValidateStateModificationContext("Test operation");
    }

    /// <summary>
    /// Simulates event handler execution
    /// </summary>
    public async Task SimulateEventHandler(TestEvent evt)
    {
        // Simulate what InvokeHandler does
        using var scope = StateProtectionContext.BeginEventHandlerScope();
        await HandleTestEvent(evt);
    }

    [EventHandler]
    public async Task HandleTestEvent(TestEvent evt)
    {
        // This should succeed because we're in an event handler
        State.Name = evt.Payload;
        await Task.CompletedTask;
    }
}