using Aevatar.Agents.Abstractions.Attributes;

namespace Aevatar.Agents.Core.Tests.Agents;

/// <summary>
/// Test agent for priority testing
/// </summary>
public class PriorityTestAgent : GAgentBase<TestAgentState>
{
    public List<string> ExecutionOrder { get; } = new();
    
    public override string GetDescription() => "PriorityTestAgent";
    
    [EventHandler(Priority = 1)]
    public async Task HighPriorityHandler(TestEvent evt)
    {
        ExecutionOrder.Add("High");
        await Task.CompletedTask;
    }
    
    [EventHandler(Priority = 5)]
    public async Task MediumPriorityHandler(TestEvent evt)
    {
        ExecutionOrder.Add("Medium");
        await Task.CompletedTask;
    }
    
    [EventHandler(Priority = 10)]
    public async Task LowPriorityHandler(TestEvent evt)
    {
        ExecutionOrder.Add("Low");
        await Task.CompletedTask;
    }
    
    [AllEventHandler(Priority = 100)]
    public async Task AllEventHandlerMethod(EventEnvelope envelope)
    {
        ExecutionOrder.Add("All");
        await Task.CompletedTask;
    }
}