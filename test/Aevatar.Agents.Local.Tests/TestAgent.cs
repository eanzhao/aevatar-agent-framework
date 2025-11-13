using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Local.Tests;

public class TestState
{
    public int Version { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class TestAgent : GAgentBase<TestState>
{
    public TestAgent(Guid id, ILogger<TestAgent>? logger = null) 
        : base(id, logger)
    {
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Test Agent for Local runtime tests");
    }
    
    [EventHandler]
    public Task HandleConfigEventAsync(GeneralConfigEvent evt)
    {
        State.Name = evt.ConfigKey;
        return Task.CompletedTask;
    }
}
