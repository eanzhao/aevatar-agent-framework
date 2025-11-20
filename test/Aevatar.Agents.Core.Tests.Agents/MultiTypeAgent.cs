using Aevatar.Agents.Abstractions.Attributes;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Core.Tests.Agents;

/// <summary>
/// Test agent for multiple event types
/// </summary>
public class MultiTypeAgent : GAgentBase<TestAgentState>
{
    public int TestEventCount { get; private set; }
    public int TestCommandCount { get; private set; }
    public int StringValueCount { get; private set; }
    
    public override string GetDescription() => "MultiTypeAgent";
    
    [EventHandler]
    public async Task HandleTestEvent(TestEvent evt)
    {
        TestEventCount++;
        await Task.CompletedTask;
    }
    
    [EventHandler]
    public async Task HandleTestCommand(TestCommand cmd)
    {
        TestCommandCount++;
        await Task.CompletedTask;
    }
    
    [EventHandler]
    public async Task HandleStringValue(StringValue str)
    {
        StringValueCount++;
        await Task.CompletedTask;
    }
}