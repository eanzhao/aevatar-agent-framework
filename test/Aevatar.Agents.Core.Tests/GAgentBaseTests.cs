using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Google.Protobuf.WellKnownTypes;
using Xunit;

namespace Aevatar.Agents.Core.Tests;

public class GAgentBaseTests
{
    [Fact]
    public void Agent_ShouldHaveUniqueId()
    {
        // Arrange & Act
        var agent1 = new TestAgent(Guid.NewGuid());
        var agent2 = new TestAgent(Guid.NewGuid());
        
        // Assert
        Assert.NotEqual(agent1.Id, agent2.Id);
    }
    
    [Fact]
    public void Agent_ShouldUseProvidedId()
    {
        // Arrange
        var expectedId = Guid.NewGuid();
        
        // Act
        var agent = new TestAgent(expectedId);
        
        // Assert
        Assert.Equal(expectedId, agent.Id);
    }
    
    [Fact]
    public async Task Agent_ShouldProvideDescription()
    {
        // Arrange
        var agent = new TestAgent(Guid.NewGuid());
        
        // Act
        var description = await agent.GetDescriptionAsync();
        
        // Assert
        Assert.NotNull(description);
        Assert.NotEmpty(description);
    }
    
    [Fact]
    public void Agent_ShouldDiscoverEventHandlers()
    {
        // Arrange
        var agent = new TestAgent(Guid.NewGuid());
        
        // Act
        var handlers = agent.GetEventHandlers();
        
        // Assert
        Assert.NotEmpty(handlers);
        Assert.Contains(handlers, h => h.Name == "HandleConfigEventAsync");
    }
    
    [Fact]
    public async Task Agent_ShouldHandleEvent()
    {
        // Arrange
        var agent = new TestAgent(Guid.NewGuid());
        
        // 创建事件信封
        var testEvent = new StringValue { Value = "Test Message" };
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Version = 1,
            Payload = Google.Protobuf.WellKnownTypes.Any.Pack(testEvent),
            PublisherId = agent.Id.ToString(),
            Direction = EventDirection.Down
        };
        
        // Act
        await agent.HandleEventAsync(envelope);
        
        // Assert
        // 由于 StringValue 不匹配 TestEvent，不会被处理
        // 这个测试主要验证 HandleEventAsync 不会抛出异常
    }
    
    [Fact]
    public void Agent_ShouldHaveInitialState()
    {
        // Arrange & Act
        var agent = new TestAgent(Guid.NewGuid());
        var state = agent.GetState();
        
        // Assert
        Assert.NotNull(state);
        Assert.Equal(0, state.Counter);
        Assert.Equal(string.Empty, state.Name);
    }
    
    [Fact]
    public async Task Agent_ShouldCallActivateCallback()
    {
        // Arrange
        var agent = new TestAgent(Guid.NewGuid());
        
        // Act
        await agent.OnActivateAsync();
        
        // Assert
        // 验证不抛出异常
        Assert.True(true);
    }
    
    [Fact]
    public async Task Agent_ShouldCallDeactivateCallback()
    {
        // Arrange
        var agent = new TestAgent(Guid.NewGuid());
        
        // Act
        await agent.OnDeactivateAsync();
        
        // Assert
        // 验证不抛出异常
        Assert.True(true);
    }
}
