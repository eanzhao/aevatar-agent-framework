using Aevatar.Agents.Abstractions;

namespace Aevatar.Agents.Core.Tests;

public class TestState
{
    public int Version { get; set; }
    public string Value { get; set; } = string.Empty;
}

public class TestAgent : GAgentBase<TestState>
{
    public int ApplyEventCallCount { get; private set; }

    public TestAgent(
        IServiceProvider serviceProvider,
        IGAgentFactory factory,
        IMessageSerializer serializer)
        : base(serviceProvider, factory, serializer)
    {
    }

    public override Task RegisterEventHandlersAsync(IMessageStream stream, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public override Task ApplyEventAsync(EventEnvelope evt, CancellationToken ct = default)
    {
        ApplyEventCallCount++;
        _state.Version = (int)evt.Version;
        return Task.CompletedTask;
    }
}