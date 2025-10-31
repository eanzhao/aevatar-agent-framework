using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core.Tests;

public class TestState
{
    public string Name { get; set; } = string.Empty;
    public int Counter { get; set; }
}

public class TestAgent : GAgentBase<TestState>
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
        _state.Counter++;
        _state.Name = evt.ConfigKey;
        return Task.CompletedTask;
    }
}