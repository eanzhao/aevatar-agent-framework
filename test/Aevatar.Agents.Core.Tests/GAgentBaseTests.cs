using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Aevatar.Agents.Core.Tests;

public class GAgentBaseTests
{
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<IGAgentFactory> _mockFactory;
    private readonly Mock<IMessageSerializer> _mockSerializer;
    private readonly Mock<IMessageStream> _mockStream;

    public GAgentBaseTests()
    {
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockFactory = new Mock<IGAgentFactory>();
        _mockSerializer = new Mock<IMessageSerializer>();
        _mockStream = new Mock<IMessageStream>();

        _mockServiceProvider
            .Setup(sp => sp.GetService(typeof(IMessageStream)))
            .Returns(_mockStream.Object);

        var serviceScope = new Mock<IServiceScope>();
        serviceScope.Setup(s => s.ServiceProvider).Returns(_mockServiceProvider.Object);

        var serviceScopeFactory = new Mock<IServiceScopeFactory>();
        serviceScopeFactory.Setup(f => f.CreateScope()).Returns(serviceScope.Object);

        _mockServiceProvider
            .Setup(sp => sp.GetService(typeof(IServiceScopeFactory)))
            .Returns(serviceScopeFactory.Object);
    }

    [Fact]
    public async Task AddSubAgentAsync_AddsSubAgentAndRaisesEvent()
    {
        // Arrange
        var testAgent = new TestAgent(_mockServiceProvider.Object, _mockFactory.Object, _mockSerializer.Object);
        var mockSubAgent = new TestAgent(_mockServiceProvider.Object, _mockFactory.Object, _mockSerializer.Object);

        var mockSubAgentActor = new Mock<IGAgentActor>();
        var mockParentActor = new Mock<IGAgentActor>();

        _mockFactory.Setup(f => f.CreateAgentAsync<TestAgent, TestState>(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockSubAgentActor.Object);

        _mockServiceProvider
            .Setup(sp => sp.GetService(typeof(TestAgent)))
            .Returns(mockSubAgent);

        _mockServiceProvider
            .Setup(sp => sp.GetService(typeof(IGAgentActor)))
            .Returns(mockParentActor.Object);

        // Act
        await testAgent.AddSubAgentAsync<TestAgent, TestState>();

        // Assert
        Assert.Single(testAgent.GetSubAgents());
        Assert.Single(testAgent.GetPendingEvents());

        // Verify the event has the right structure
        var evt = testAgent.GetPendingEvents()[0];
        Assert.Equal(1, evt.Version);
        Assert.NotNull(evt.Payload);

        // Check if the payload can be unpacked as SubAgentAdded
        if (evt.Payload.Is(SubAgentAdded.Descriptor))
        {
            var payload = evt.Payload.Unpack<SubAgentAdded>();
            Assert.Equal(mockSubAgent.Id.ToString(), payload.SubAgentId);
        }

        // Verify the actor was subscribed to parent stream
        mockSubAgentActor.Verify(a => a.SubscribeToParentStreamAsync(
                mockParentActor.Object,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RemoveSubAgentAsync_RemovesSubAgentAndRaisesEvent()
    {
        // Arrange
        var testAgent = new TestAgent(_mockServiceProvider.Object, _mockFactory.Object, _mockSerializer.Object);
        var mockSubAgent = new TestAgent(_mockServiceProvider.Object, _mockFactory.Object, _mockSerializer.Object);
        var subAgentId = mockSubAgent.Id; // Use the actual Id from the mock agent

        // Use reflection to access the protected _subAgents field from the base class
        var subAgentsField = typeof(GAgentBase<TestState>)
            .GetField("_subAgents",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var subAgents = subAgentsField?.GetValue(testAgent) as List<IGAgent>;
        subAgents?.Add(mockSubAgent);

        // Act
        await testAgent.RemoveSubAgentAsync(subAgentId);

        // Assert
        Assert.Empty(testAgent.GetSubAgents());
        Assert.Single(testAgent.GetPendingEvents());

        // Verify the event has the right structure
        var evt = testAgent.GetPendingEvents()[0];
        Assert.Equal(1, evt.Version);
        Assert.NotNull(evt.Payload);

        // Check if the payload can be unpacked as SubAgentRemoved
        if (evt.Payload.Is(SubAgentRemoved.Descriptor))
        {
            var payload = evt.Payload.Unpack<SubAgentRemoved>();
            Assert.Equal(subAgentId.ToString(), payload.SubAgentId);
        }
    }

    [Fact]
    public async Task RaiseEventAsync_AddsEventToPendingEvents()
    {
        // Arrange
        var testAgent = new TestAgent(_mockServiceProvider.Object, _mockFactory.Object, _mockSerializer.Object);
        var testEvent = new GeneralConfigEvent { ConfigKey = "test", ConfigValue = "value" };

        // Act
        await testAgent.RaiseEventAsync(testEvent);

        // Assert
        Assert.Single(testAgent.GetPendingEvents());

        // Verify ApplyEventAsync was called
        Assert.Equal(1, testAgent.ApplyEventCallCount);
    }

    [Fact]
    public async Task ProduceEventAsync_SendsEventToStream()
    {
        // Arrange
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(IMessageStream)))
            .Returns(_mockStream.Object);

        var testAgent = new TestAgent(_mockServiceProvider.Object, _mockFactory.Object, _mockSerializer.Object);
        var testMessage = new StringValue { Value = "Test message" };

        // Act
        await testAgent.ProduceEventAsync(testMessage);

        // Assert
        _mockStream.Verify(s => s.ProduceAsync(
                It.IsAny<IMessage>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}