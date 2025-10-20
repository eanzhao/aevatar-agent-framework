using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Moq;

namespace Aevatar.Agents.Local.Tests;

public class LocalGAgentActorTests
{
    private readonly Mock<IMessageStream> _mockStream;
    private readonly Mock<IGAgent<TestState>> _mockBusinessAgent;
    private readonly Mock<IGAgentFactory> _mockFactory;
    private readonly Dictionary<Guid, List<EventEnvelope>> _eventStore;
    private readonly Guid _agentId = Guid.NewGuid();

    public LocalGAgentActorTests()
    {
        _mockStream = new Mock<IMessageStream>();
        _mockBusinessAgent = new Mock<IGAgent<TestState>>();
        _mockFactory = new Mock<IGAgentFactory>();
        _eventStore = new Dictionary<Guid, List<EventEnvelope>>();
            
        _mockBusinessAgent.Setup(a => a.Id).Returns(_agentId);
    }

    [Fact]
    public void Constructor_InitializesEventStoreEntry()
    {
        // Arrange & Act
        var actor = new LocalGAgentActor<TestState>(
            _mockStream.Object,
            _mockBusinessAgent.Object,
            _mockFactory.Object,
            _eventStore);

        // Assert
        Assert.Contains(_agentId, _eventStore.Keys);
        Assert.Empty(_eventStore[_agentId]);
        Assert.Equal(_agentId, actor.Id);
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
            
        var pendingEvents = new List<EventEnvelope>
        {
            new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Version = 1,
                Payload = Any.Pack(new SubAgentAdded { SubAgentId = subAgentId.ToString() })
            }
        };
            
        _mockBusinessAgent
            .Setup(a => a.AddSubAgentAsync<TestSubAgent, TestSubState>(
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
                
        _mockBusinessAgent
            .Setup(a => a.GetPendingEvents())
            .Returns(pendingEvents);
            
        var actor = new LocalGAgentActor<TestState>(
            _mockStream.Object,
            _mockBusinessAgent.Object,
            _mockFactory.Object,
            _eventStore);

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
                
        Assert.Single(_eventStore[_agentId]);
        Assert.Equal(pendingEvents[0], _eventStore[_agentId][0]);
    }

    [Fact]
    public async Task RemoveSubAgentAsync_RemovesSubAgent()
    {
        // Arrange
        var subAgentId = Guid.NewGuid();
        var mockSubAgentActor = new Mock<IGAgentActor>();
        mockSubAgentActor.Setup(a => a.Id).Returns(subAgentId);
            
        // Set up a private dictionary field in the actor using reflection
        var actor = new LocalGAgentActor<TestState>(
            _mockStream.Object,
            _mockBusinessAgent.Object,
            _mockFactory.Object,
            _eventStore);
                
        var subAgentDict = new Dictionary<Guid, IGAgentActor>
        {
            { subAgentId, mockSubAgentActor.Object }
        };
            
        typeof(LocalGAgentActor<TestState>)
            .GetField("_subAgents", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(actor, subAgentDict);
                
        var pendingEvents = new List<EventEnvelope>
        {
            new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Version = 1,
                Payload = Any.Pack(new SubAgentRemoved { SubAgentId = subAgentId.ToString() })
            }
        };
            
        _mockBusinessAgent
            .Setup(a => a.RemoveSubAgentAsync(
                subAgentId, 
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
                
        _mockBusinessAgent
            .Setup(a => a.GetPendingEvents())
            .Returns(pendingEvents);

        // Act
        await actor.RemoveSubAgentAsync(subAgentId);

        // Assert
        _mockBusinessAgent.Verify(a => a.RemoveSubAgentAsync(
                subAgentId, 
                It.IsAny<CancellationToken>()), 
            Times.Once);
                
        Assert.Single(_eventStore[_agentId]);
        Assert.Equal(pendingEvents[0], _eventStore[_agentId][0]);
            
        // Verify the sub agent was removed from the dictionary
        var subAgents = typeof(LocalGAgentActor<TestState>)
            .GetField("_subAgents", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(actor) as Dictionary<Guid, IGAgentActor>;
                
        Assert.Empty(subAgents);
    }

    [Fact]
    public async Task ProduceEventAsync_ForwardsToStream()
    {
        // Arrange
        var message = new StringValue { Value = "Test message" };
        var actor = new LocalGAgentActor<TestState>(
            _mockStream.Object,
            _mockBusinessAgent.Object,
            _mockFactory.Object,
            _eventStore);
                
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
        var actor = new LocalGAgentActor<TestState>(
            _mockStream.Object,
            _mockBusinessAgent.Object,
            _mockFactory.Object,
            _eventStore);
                
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