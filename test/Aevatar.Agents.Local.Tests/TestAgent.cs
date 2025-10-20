using Aevatar.Agents.Abstractions;
using Google.Protobuf;

namespace Aevatar.Agents.Local.Tests;

// Using different mock classes because Moq has issues with mocking generic interfaces with constraints
public class TestState
{
    public int Version { get; set; }
}

public class TestAgent : IGAgent<TestState>
{
    public int ApplyEventCallCount { get; private set; }

    public Guid Id => Guid.NewGuid();

    private readonly IServiceProvider _serviceProvider;
    private readonly IGAgentFactory _factory;
    private readonly IMessageSerializer _serializer;

    public TestAgent(IServiceProvider serviceProvider, IGAgentFactory factory, IMessageSerializer serializer)
    {
        _serviceProvider = serviceProvider;
        _factory = factory;
        _serializer = serializer;
    }

    public Task RegisterEventHandlersAsync(IMessageStream stream, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public Task AddSubAgentAsync<TSubAgent, TSubState>(CancellationToken ct = default)
        where TSubAgent : IGAgent<TSubState>
        where TSubState : class, new()
    {
        return Task.CompletedTask;
    }

    public Task RemoveSubAgentAsync(Guid subAgentId, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public IReadOnlyList<IGAgent> GetSubAgents()
    {
        return new List<IGAgent>();
    }

    public TestState GetState()
    {
        return new TestState();
    }

    public IReadOnlyList<EventEnvelope> GetPendingEvents()
    {
        return new List<EventEnvelope>();
    }

    public Task RaiseEventAsync<TEvent>(TEvent evt, CancellationToken ct = default) where TEvent : class
    {
        return Task.CompletedTask;
    }

    public Task ApplyEventAsync(EventEnvelope evt, CancellationToken ct = default)
    {
        ApplyEventCallCount++;
        return Task.CompletedTask;
    }

    public Task ProduceEventAsync(IMessage message, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}