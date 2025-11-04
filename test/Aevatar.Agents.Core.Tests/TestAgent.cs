using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Tests.Messages;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core.Tests;

public class TestAgent : GAgentBase<Messages.TestState>
{
    public int EventHandledCount { get; private set; }
    
    public TestAgent(Guid id, ILogger<TestAgent>? logger = null) 
        : base(id, logger)
    {
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Test Agent for unit testing");
    }
    
    // 测试事件处理器（使用 Protobuf 生成的类型）
    [EventHandler]
    public Task HandleConfigEventAsync(GeneralConfigEvent evt)
    {
        EventHandledCount++;
        State.Counter++;
        State.Name = evt.ConfigKey;
        return Task.CompletedTask;
    }
    
    [EventHandler(Priority = 1)]
    public Task HandleTestEventAsync(TestEvent evt)
    {
        EventHandledCount++;
        State.Name = evt.EventData;
        return Task.CompletedTask;
    }
    
    [EventHandler(AllowSelfHandling = true)]
    public Task HandleTestAddItemEventAsync(TestAddItemEvent evt)
    {
        EventHandledCount++;
        State.Items.Add(evt.ItemName);
        return Task.CompletedTask;
    }
    
    [AllEventHandler]
    public Task HandleAllEventsAsync(EventEnvelope envelope)
    {
        EventHandledCount++;
        State.Counter++;
        return Task.CompletedTask;
    }
}