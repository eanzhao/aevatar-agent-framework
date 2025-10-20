using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Moq;
using Proto;

namespace Aevatar.Agents.ProtoActor.Tests;

public class ProtoActorGAgentActorTests
{
    private readonly Mock<IGAgent<TestState>> _mockBusinessAgent;
    private readonly Mock<IRootContext> _mockRootContext;
    private readonly PID _actorPid;
    private readonly Mock<IGAgentFactory> _mockFactory;
    private readonly Mock<IMessageStream> _mockStream;

    public ProtoActorGAgentActorTests()
    {
        _mockBusinessAgent = new Mock<IGAgent<TestState>>();
        _mockRootContext = new Mock<IRootContext>();
        _actorPid = new PID("address", "id");
        _mockFactory = new Mock<IGAgentFactory>();
        _mockStream = new Mock<IMessageStream>();

        _mockBusinessAgent.Setup(a => a.Id).Returns(Guid.NewGuid());
    }

    [Fact]
    public void Constructor_InitializesProperties()
    {
        // Arrange & Act
        var actor = new ProtoActorGAgentActor<TestState>(
            _mockBusinessAgent.Object,
            _mockRootContext.Object,
            _actorPid,
            _mockFactory.Object,
            _mockStream.Object);

        // Assert
        Assert.Equal(_mockBusinessAgent.Object.Id, actor.Id);
        Assert.Equal(_actorPid, actor.GetActorPid());
    }

    [Fact]
    public async Task AddSubAgentAsync_CreatesAndSubscribesSubAgent()
    {
        // Arrange
        var subAgentId = Guid.NewGuid();
        var mockSubAgentActor = new Mock<IGAgentActor>();
        mockSubAgentActor.Setup(a => a.Id).Returns(subAgentId);

        _mockFactory
            .Setup(f => f.CreateAgentAsync<TestSubAgent, TestSubState>(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockSubAgentActor.Object);

        var actor = new ProtoActorGAgentActor<TestState>(
            _mockBusinessAgent.Object,
            _mockRootContext.Object,
            _actorPid,
            _mockFactory.Object,
            _mockStream.Object);

        // Act
        await actor.AddSubAgentAsync<TestSubAgent, TestSubState>();

        // Assert
        _mockFactory.Verify(f => f.CreateAgentAsync<TestSubAgent, TestSubState>(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _mockBusinessAgent.Verify(a => a.AddSubAgentAsync<TestSubAgent, TestSubState>(
                It.IsAny<CancellationToken>()),
            Times.Once);

        mockSubAgentActor.Verify(a => a.SubscribeToParentStreamAsync(
                actor,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RemoveSubAgentAsync_RemovesSubAgent()
    {
        // Arrange
        var subAgentId = Guid.NewGuid();
        var mockSubAgentPid = new PID("address", "subagent");

        var actor = new ProtoActorGAgentActor<TestState>(
            _mockBusinessAgent.Object,
            _mockRootContext.Object,
            _actorPid,
            _mockFactory.Object,
            _mockStream.Object);

        // Set up a private dictionary field in the actor using reflection
        var subAgentDict = new Dictionary<Guid, PID>
        {
            { subAgentId, mockSubAgentPid }
        };

        typeof(ProtoActorGAgentActor<TestState>)
            .GetField("_subAgentPids",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(actor, subAgentDict);

        // Act
        await actor.RemoveSubAgentAsync(subAgentId);

        // Assert
        _mockBusinessAgent.Verify(a => a.RemoveSubAgentAsync(
                subAgentId,
                It.IsAny<CancellationToken>()),
            Times.Once);

        _mockRootContext.Verify(r => r.StopAsync(mockSubAgentPid), Times.Once);

        // Verify the sub agent was removed from the dictionary
        var subAgents = typeof(ProtoActorGAgentActor<TestState>)
            .GetField("_subAgentPids",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(actor) as Dictionary<Guid, PID>;

        Assert.Empty(subAgents);
    }

    [Fact]
    public async Task ProduceEventAsync_ForwardsToStream()
    {
        // Arrange
        var message = new StringValue { Value = "Test message" };
        var actor = new ProtoActorGAgentActor<TestState>(
            _mockBusinessAgent.Object,
            _mockRootContext.Object,
            _actorPid,
            _mockFactory.Object,
            _mockStream.Object);

        // Act
        await actor.ProduceEventAsync(message);

        // Assert
        // The actual call uses IMessage not StringValue
        _mockStream.Verify(s => s.ProduceAsync<IMessage>(
                It.IsAny<IMessage>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SubscribeToParentStreamAsync_RegistersHandlers()
    {
        // Arrange
        var mockParent = new Mock<IGAgentActor>();
        var actor = new ProtoActorGAgentActor<TestState>(
            _mockBusinessAgent.Object,
            _mockRootContext.Object,
            _actorPid,
            _mockFactory.Object,
            _mockStream.Object);

        // Act
        await actor.SubscribeToParentStreamAsync(mockParent.Object);

        // Assert
        _mockBusinessAgent.Verify(a => a.RegisterEventHandlersAsync(
                _mockStream.Object,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    public class TestState
    {
        public int Version { get; set; }
    }

    public class TestSubAgent : IGAgent<TestSubState>
    {
        public Guid Id => Guid.NewGuid();

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

        public TestSubState GetState()
        {
            return new TestSubState();
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
            return Task.CompletedTask;
        }

        public Task ProduceEventAsync(IMessage message, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }

    public class TestSubState
    {
        public int Version { get; set; }
    }
}