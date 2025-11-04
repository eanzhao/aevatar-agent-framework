using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Helpers;
using Aevatar.Agents.Core.Tests.Messages;
using FluentAssertions;
using Moq;
using Xunit;

namespace Aevatar.Agents.Core.Tests.Helpers;

public class AgentTypeHelperTests
{
    [Fact(DisplayName = "ExtractStateType should extract state type from agent implementing IGAgent<TState>")]
    public void ExtractStateType_ShouldExtractStateTypeFromAgentImplementingIGAgent()
    {
        // Arrange
        var agentType = typeof(TestAgent);
        
        // Act
        var stateType = AgentTypeHelper.ExtractStateType(agentType);
        
        // Assert
        stateType.Should().Be(typeof(TestState));
    }
    
    [Fact(DisplayName = "ExtractStateType should extract state type from derived agent classes")]
    public void ExtractStateType_ShouldExtractStateTypeFromDerivedAgentClasses()
    {
        // Arrange
        var agentType = typeof(DerivedTestAgent);
        
        // Act
        var stateType = AgentTypeHelper.ExtractStateType(agentType);
        
        // Assert
        stateType.Should().Be(typeof(TestState));
    }
    
    [Fact(DisplayName = "ExtractStateType should extract state type from deeply nested inheritance")]
    public void ExtractStateType_ShouldExtractStateTypeFromDeeplyNestedInheritance()
    {
        // Arrange
        var agentType = typeof(DeeplyDerivedAgent);
        
        // Act
        var stateType = AgentTypeHelper.ExtractStateType(agentType);
        
        // Assert
        stateType.Should().Be(typeof(TestState));
    }
    
    [Fact(DisplayName = "ExtractStateType should work with different state types")]
    public void ExtractStateType_ShouldWorkWithDifferentStateTypes()
    {
        // Arrange
        var agentType1 = typeof(AgentWithTestState);
        var agentType2 = typeof(AgentWithConfigState);
        var agentType3 = typeof(AgentWithEventSourcingState);
        
        // Act
        var stateType1 = AgentTypeHelper.ExtractStateType(agentType1);
        var stateType2 = AgentTypeHelper.ExtractStateType(agentType2);
        var stateType3 = AgentTypeHelper.ExtractStateType(agentType3);
        
        // Assert
        stateType1.Should().Be(typeof(TestState));
        stateType2.Should().Be(typeof(TestConfigState));
        stateType3.Should().Be(typeof(TestEventSourcingState));
    }
    
    [Fact(DisplayName = "ExtractStateType should throw for types not implementing IGAgent<TState>")]
    public void ExtractStateType_ShouldThrowForTypesNotImplementingIGAgent()
    {
        // Arrange
        var invalidType = typeof(NonAgentClass);
        
        // Act
        var act = () => AgentTypeHelper.ExtractStateType(invalidType);
        
        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{invalidType.Name}*does not implement IGAgent<TState>*");
    }
    
    [Fact(DisplayName = "ExtractStateType should work with generic agent base classes")]
    public void ExtractStateType_ShouldWorkWithGenericAgentBaseClasses()
    {
        // Arrange
        var agentType = typeof(GenericDerivedAgent);
        
        // Act
        var stateType = AgentTypeHelper.ExtractStateType(agentType);
        
        // Assert
        stateType.Should().Be(typeof(TestState));
    }
    
    [Fact(DisplayName = "InvokeCreateAgentAsync should create agent using reflection")]
    public async Task InvokeCreateAgentAsync_ShouldCreateAgentUsingReflection()
    {
        // Arrange
        var mockFactory = new Mock<IGAgentActorFactory>();
        var mockActor = new Mock<IGAgentActor>();
        var agentId = Guid.NewGuid();
        var agentType = typeof(TestAgent);
        var stateType = typeof(TestState);
        
        mockFactory
            .Setup(f => f.CreateGAgentActorAsync<TestAgent, TestState>(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockActor.Object);
        
        // Act
        var result = await AgentTypeHelper.InvokeCreateAgentAsync(
            mockFactory.Object, 
            agentType, 
            stateType, 
            agentId);
        
        // Assert
        result.Should().Be(mockActor.Object);
        mockFactory.Verify(
            f => f.CreateGAgentActorAsync<TestAgent, TestState>(agentId, It.IsAny<CancellationToken>()), 
            Times.Once);
    }
    
    [Fact(DisplayName = "InvokeCreateAgentAsync should work with different agent and state types")]
    public async Task InvokeCreateAgentAsync_ShouldWorkWithDifferentAgentAndStateTypes()
    {
        // Arrange
        var mockFactory = new Mock<IGAgentActorFactory>();
        var mockActor = new Mock<IGAgentActor>();
        var agentId = Guid.NewGuid();
        var agentType = typeof(AgentWithConfigState);
        var stateType = typeof(TestConfigState);
        
        // Setup using reflection to handle the generic method call
        var createMethod = typeof(IGAgentActorFactory)
            .GetMethods()
            .First(m => m.Name == "CreateGAgentActorAsync" && m.GetGenericArguments().Length == 2)
            .MakeGenericMethod(agentType, stateType);
        
        mockFactory
            .Setup(f => f.CreateGAgentActorAsync<AgentWithConfigState, TestConfigState>(
                It.IsAny<Guid>(), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockActor.Object);
        
        // Act
        var result = await AgentTypeHelper.InvokeCreateAgentAsync(
            mockFactory.Object, 
            agentType, 
            stateType, 
            agentId);
        
        // Assert
        result.Should().Be(mockActor.Object);
    }
    
    [Fact(DisplayName = "InvokeCreateAgentAsync should pass cancellation token correctly")]
    public async Task InvokeCreateAgentAsync_ShouldPassCancellationTokenCorrectly()
    {
        // Arrange
        var mockFactory = new Mock<IGAgentActorFactory>();
        var mockActor = new Mock<IGAgentActor>();
        var agentId = Guid.NewGuid();
        var agentType = typeof(TestAgent);
        var stateType = typeof(TestState);
        var cts = new CancellationTokenSource();
        
        mockFactory
            .Setup(f => f.CreateGAgentActorAsync<TestAgent, TestState>(agentId, cts.Token))
            .ReturnsAsync(mockActor.Object);
        
        // Act
        var result = await AgentTypeHelper.InvokeCreateAgentAsync(
            mockFactory.Object, 
            agentType, 
            stateType, 
            agentId, 
            cts.Token);
        
        // Assert
        result.Should().Be(mockActor.Object);
        mockFactory.Verify(
            f => f.CreateGAgentActorAsync<TestAgent, TestState>(agentId, cts.Token), 
            Times.Once);
    }
}

// Test helper classes
internal class DerivedTestAgent : TestAgent
{
    public DerivedTestAgent(Guid id) : base(id) { }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Derived Test Agent");
    }
}

internal class IntermediateAgent : TestAgent
{
    public IntermediateAgent(Guid id) : base(id) { }
}

internal class DeeplyDerivedAgent : IntermediateAgent
{
    public DeeplyDerivedAgent(Guid id) : base(id) { }
}

internal class AgentWithTestState : GAgentBase<TestState>
{
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Agent with TestState");
    }
}

internal class AgentWithConfigState : GAgentBase<TestConfigState>
{
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Agent with TestConfigState");
    }
}

internal class AgentWithEventSourcingState : GAgentBase<TestEventSourcingState>
{
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Agent with TestEventSourcingState");
    }
}

internal class GenericBaseAgent<TState> : GAgentBase<TState>
    where TState : class, new()
{
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Generic Base Agent");
    }
}

internal class GenericDerivedAgent : GenericBaseAgent<TestState>
{
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Generic Derived Agent");
    }
}

internal class NonAgentClass
{
    public string Name { get; set; } = string.Empty;
}
