namespace Aevatar.Agents.Core.Tests.Agents;

/// <summary>
/// Test agent for convention-based handler discovery
/// </summary>
public class ConventionBasedAgent : GAgentBase<TestAgentState>
{
    public int HandleAsyncCallCount { get; private set; }
    public int HandleEventAsyncCallCount { get; private set; }

    public override string GetDescription() => "ConventionBasedAgent";

    // Should be automatically discovered (naming convention)
    public async Task HandleAsync(TestEvent evt)
    {
        HandleAsyncCallCount++;
        State.Counter++;
        await Task.CompletedTask;
    }

    // Should be automatically discovered (naming convention)
    public async Task HandleEventAsync(TestCommand cmd)
    {
        HandleEventAsyncCallCount++;
        State.Counter += 10;
        await Task.CompletedTask;
    }

    // Should NOT be discovered (doesn't follow convention)
    public async Task ProcessEvent(TestEvent evt)
    {
        // This should not be discovered
        await Task.CompletedTask;
    }
}