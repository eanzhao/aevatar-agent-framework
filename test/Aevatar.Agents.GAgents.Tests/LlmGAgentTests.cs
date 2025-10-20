using Aevatar.Agents.Abstractions;
using Google.Protobuf.WellKnownTypes;
using Moq;

namespace Aevatar.Agents.GAgents.Tests;

public class LlmGAgentTests
{
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<IGAgentFactory> _mockFactory;
    private readonly Mock<IMessageSerializer> _mockSerializer;
    private readonly Mock<IMessageStream> _mockStream;

    public LlmGAgentTests()
    {
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockFactory = new Mock<IGAgentFactory>();
        _mockSerializer = new Mock<IMessageSerializer>();
        _mockStream = new Mock<IMessageStream>();
    }

    [Fact]
    public void Constructor_InitializesState()
    {
        // Arrange & Act
        var agent = new LlmGAgent(_mockServiceProvider.Object, _mockFactory.Object, _mockSerializer.Object);

        // Assert
        Assert.NotNull(agent);
        Assert.NotEqual(Guid.Empty, agent.Id);
            
        var state = agent.GetState();
        Assert.NotNull(state);
        Assert.Empty(state.SubAgentIds);
        Assert.Equal(0, state.CurrentVersion);
        Assert.Empty(state.LlmConfig);
    }

    [Fact]
    public async Task RegisterEventHandlersAsync_SubscribesToEvents()
    {
        // Arrange
        var agent = new LlmGAgent(_mockServiceProvider.Object, _mockFactory.Object, _mockSerializer.Object);

        // Act
        await agent.RegisterEventHandlersAsync(_mockStream.Object);

        // Assert
        // Verify subscriptions were created for expected event types
        _mockStream.Verify(s => s.SubscribeAsync<LLMEvent>(
                It.IsAny<Func<LLMEvent, Task>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
                
        _mockStream.Verify(s => s.SubscribeAsync<GeneralConfigEvent>(
                It.IsAny<Func<GeneralConfigEvent, Task>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ApplyEventAsync_LlmEvent_UpdatesState()
    {
        // Arrange
        var agent = new LlmGAgent(_mockServiceProvider.Object, _mockFactory.Object, _mockSerializer.Object);
        var llmEvent = new LLMEvent
        {
            Prompt = "Test prompt",
            Response = "Test response"
        };
            
        var evt = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Version = 1,
            Payload = Any.Pack(llmEvent)
        };

        // Act
        await agent.ApplyEventAsync(evt);

        // Assert
        var state = agent.GetState();
        Assert.Equal(llmEvent.Response, state.LlmConfig);
        Assert.Equal(evt.Version, state.CurrentVersion);
    }

    [Fact]
    public async Task ApplyEventAsync_ConfigEvent_UpdatesState()
    {
        // Arrange
        var agent = new LlmGAgent(_mockServiceProvider.Object, _mockFactory.Object, _mockSerializer.Object);
        var configEvent = new GeneralConfigEvent
        {
            ConfigKey = "api_key",
            ConfigValue = "sk-12345"
        };
            
        var evt = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Version = 1,
            Payload = Any.Pack(configEvent)
        };

        // Act
        await agent.ApplyEventAsync(evt);

        // Assert
        var state = agent.GetState();
        Assert.Equal(configEvent.ConfigValue, state.LlmConfig);
        Assert.Equal(evt.Version, state.CurrentVersion);
    }

    [Fact]
    public async Task ApplyEventAsync_SubAgentAddedEvent_UpdatesState()
    {
        // Arrange
        var agent = new LlmGAgent(_mockServiceProvider.Object, _mockFactory.Object, _mockSerializer.Object);
        var subAgentId = Guid.NewGuid().ToString();
        var addEvent = new SubAgentAdded
        {
            SubAgentId = subAgentId
        };
            
        var evt = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Version = 1,
            Payload = Any.Pack(addEvent)
        };

        // Act
        await agent.ApplyEventAsync(evt);

        // Assert
        var state = agent.GetState();
        Assert.Single(state.SubAgentIds);
        Assert.Equal(subAgentId, state.SubAgentIds[0]);
        Assert.Equal(evt.Version, state.CurrentVersion);
    }

    [Fact]
    public async Task ApplyEventAsync_SubAgentRemovedEvent_UpdatesState()
    {
        // Arrange
        var agent = new LlmGAgent(_mockServiceProvider.Object, _mockFactory.Object, _mockSerializer.Object);
        var subAgentId = Guid.NewGuid().ToString();
            
        // First add the sub agent
        var addEvent = new SubAgentAdded { SubAgentId = subAgentId };
        var addEnvelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Version = 1,
            Payload = Any.Pack(addEvent)
        };
        await agent.ApplyEventAsync(addEnvelope);
            
        // Then remove it
        var removeEvent = new SubAgentRemoved { SubAgentId = subAgentId };
        var removeEnvelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Version = 2,
            Payload = Any.Pack(removeEvent)
        };

        // Act
        await agent.ApplyEventAsync(removeEnvelope);

        // Assert
        var state = agent.GetState();
        Assert.Empty(state.SubAgentIds);
        Assert.Equal(removeEnvelope.Version, state.CurrentVersion);
    }
}