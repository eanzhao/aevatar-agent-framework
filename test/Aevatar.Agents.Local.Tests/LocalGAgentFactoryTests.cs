using Aevatar.Agents.Abstractions;
using Moq;

namespace Aevatar.Agents.Local.Tests;

public class LocalGAgentFactoryTests
{
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<IMessageSerializer> _mockSerializer;
    private readonly Mock<IGAgent<TestState>> _mockBusinessAgent;

    public LocalGAgentFactoryTests()
    {
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockSerializer = new Mock<IMessageSerializer>();
        _mockBusinessAgent = new Mock<IGAgent<TestState>>();

        _mockServiceProvider
            .Setup(sp => sp.GetService(typeof(IMessageSerializer)))
            .Returns(_mockSerializer.Object);
    }

    [Fact]
    public async Task CreateAgentAsync_CreatesAgentWithNewStream()
    {
        // Arrange
        var id = Guid.NewGuid();
        var factory = new LocalGAgentFactory(_mockServiceProvider.Object);
        var mockFactory = new Mock<IGAgentFactory>();
        var testAgent = new TestAgent(_mockServiceProvider.Object, mockFactory.Object, _mockSerializer.Object);

        _mockServiceProvider
            .Setup(sp => sp.GetService(typeof(TestAgent)))
            .Returns(testAgent);

        // Act
        var actor = await factory.CreateAgentAsync<TestAgent, TestState>(id);

        // Assert
        Assert.NotNull(actor);
        Assert.IsType<LocalGAgentActor<TestState>>(actor);

        _mockServiceProvider.Verify(sp => sp.GetService(typeof(IMessageSerializer)), Times.Once);
        _mockServiceProvider.Verify(sp => sp.GetService(typeof(TestAgent)), Times.Once);
    }

    [Fact]
    public async Task CreateAgentAsync_WithExistingEvents_AppliesEvents()
    {
        // Arrange
        var id = Guid.NewGuid();
        var factory = new LocalGAgentFactory(_mockServiceProvider.Object);
        var mockFactory = new Mock<IGAgentFactory>();
        var testAgent = new TestAgent(_mockServiceProvider.Object, mockFactory.Object, _mockSerializer.Object);

        // Create an event store and add some events for our agent
        var eventStore = new List<EventEnvelope>
        {
            new()
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Version = 1,
                Payload = Google.Protobuf.WellKnownTypes.Any.Pack(
                    new SubAgentAdded { SubAgentId = Guid.NewGuid().ToString() })
            },
            new()
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Version = 2,
                Payload = Google.Protobuf.WellKnownTypes.Any.Pack(
                    new SubAgentAdded { SubAgentId = Guid.NewGuid().ToString() })
            }
        };

        _mockServiceProvider
            .Setup(sp => sp.GetService(typeof(TestAgent)))
            .Returns(testAgent);

        // Use reflection to set the private _eventStore field in the factory
        var eventStoreDict = new Dictionary<Guid, List<EventEnvelope>>
        {
            { id, eventStore }
        };

        typeof(LocalGAgentFactory)
            .GetField("_eventStore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(factory, eventStoreDict);

        // Act
        var actor = await factory.CreateAgentAsync<TestAgent, TestState>(id);

        // Assert
        Assert.NotNull(actor);

        // Verify that events were applied by checking the test agent's call count
        Assert.Equal(eventStore.Count, testAgent.ApplyEventCallCount);
    }
}