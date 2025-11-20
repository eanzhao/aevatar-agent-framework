using Aevatar.Agents.Abstractions.Attributes;

namespace Aevatar.Agents.Core.Tests.Agents;

/// <summary>
/// 用于测试自身事件处理的Agent
/// </summary>
public class SelfEventTestAgent : GAgentBase<TestAgentState>
{
    public int SelfEventHandledCount { get; private set; }
    public int OtherEventHandledCount { get; private set; }
    
    public override string GetDescription() => "SelfEventTestAgent";
    
    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleWithSelfEvents(TestEvent evt)
    {
        State.Counter++;
        SelfEventHandledCount++;
        await Task.CompletedTask;
    }
    
    [EventHandler(AllowSelfHandling = false)]
    public async Task HandleWithoutSelfEvents(TestCommand cmd)
    {
        OtherEventHandledCount++;
        await Task.CompletedTask;
    }
}