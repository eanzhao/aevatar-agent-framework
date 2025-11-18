namespace Aevatar.Agents.Core.Tests.Agents;

/// <summary>
/// Test agent for publishing events
/// </summary>
public class PublishingTestAgent : GAgentBase<TestAgentState>
{
    public override string GetDescription() => "PublishingTestAgent";

    public async Task PublishTestEventUpAsync(TestEvent evt)
    {
        await PublishAsync(evt, EventDirection.Up);
    }

    public async Task PublishTestEventDownAsync(TestEvent evt)
    {
        await PublishAsync(evt, EventDirection.Down);
    }

    public async Task PublishTestEventBothAsync(TestEvent evt)
    {
        await PublishAsync(evt, EventDirection.Both);
    }
}