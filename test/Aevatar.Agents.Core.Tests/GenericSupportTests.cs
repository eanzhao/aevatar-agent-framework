using System;
using System.Threading.Tasks;
using Xunit;
using Shouldly;
using Aevatar.Agents.Core.Tests.Agents;
using Aevatar.Agents.Core.Tests.Fixtures;
using Aevatar.Agents.Core.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Core.Tests;

/// <summary>
/// Tests for generic type parameter support in GAgentBase
/// </summary>
public class GenericSupportTests(CoreTestFixture fixture) : IClassFixture<CoreTestFixture>
{
    private readonly IServiceProvider _serviceProvider = fixture.ServiceProvider;

    #region Single Generic Parameter Tests

    [Fact(DisplayName = "Should support single generic parameter")]
    public async Task Should_Support_Single_Generic_Parameter()
    {
        // Arrange & Act - Create agent with only TState parameter
        var agent = new BasicTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);

        await agent.ActivateAsync();

        // Assert - Agent should work with just state parameter
        agent.ShouldNotBeNull();
        agent.GetState().ShouldNotBeNull();
        agent.GetState().ShouldBeOfType<TestAgentState>();

        // Verify state can be modified
        agent.GetState().Counter = 42;
        agent.GetState().Name = "SingleGeneric";

        agent.GetState().Counter.ShouldBe(42);
        agent.GetState().Name.ShouldBe("SingleGeneric");

        // Verify event handling works
        var testEvent = new TestEvent { EventId = "test-1", EventType = "generic-test" };
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(testEvent),
            PublisherId = "test-publisher"
        };

        await agent.HandleEventAsync(envelope);
        agent.HandleEventCallCount.ShouldBe(1);
    }

    #endregion

    #region Dual Generic Parameter Tests

    [Fact(DisplayName = "Should support dual generic parameters")]
    public async Task Should_Support_Dual_Generic_Parameters()
    {
        // Arrange & Act - Create agent with TState and TConfig parameters
        var agent = new ConfigurableTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        AgentConfigStoreInjector.InjectConfigStore(agent, _serviceProvider);

        // Set configuration
        var config = new TestAgentConfig
        {
            AgentName = "ConfiguredAgent",
            MaxRetries = 3,
            TimeoutSeconds = 30,
            EnableLogging = true
        };
        config.AllowedOperations.Add("read");
        config.AllowedOperations.Add("write");
        config.Settings["environment"] = "test";

        await agent.ConfigAsync(config);
        await agent.ActivateAsync();

        // Assert - Both state and config should work
        agent.ShouldNotBeNull();
        agent.GetState().ShouldBeOfType<TestAgentState>();
        agent.GetConfig().ShouldBeOfType<TestAgentConfig>();

        // Verify configuration is applied
        var actualConfig = agent.GetConfig();
        actualConfig.AgentName.ShouldBe("ConfigurableAgent");
        actualConfig.MaxRetries.ShouldBe(3);
        actualConfig.TimeoutSeconds.ShouldBe(30);
        actualConfig.EnableLogging.ShouldBeTrue();
        actualConfig.AllowedOperations.ShouldContain("read");
        actualConfig.AllowedOperations.ShouldContain("write");
        actualConfig.Settings["environment"].ShouldBe("test");

        // Verify state works independently
        agent.GetState().Name = "DualGeneric";
        agent.GetState().Counter = 100;

        agent.GetState().Name.ShouldBe("DualGeneric");
        agent.GetState().Counter.ShouldBe(100);
    }

    #endregion

    // NOTE: There is no triple generic parameter version
    // Framework only supports:
    // - GAgentBase (no generics)
    // - GAgentBase<TState> (single generic)
    // - GAgentBase<TState, TConfig> (dual generics)

    #region Protobuf Type Constraint Tests

    [Fact(DisplayName = "TState should be Protobuf message type")]
    public async Task TState_Should_Be_Protobuf_Message_Type()
    {
        // Arrange & Act - Verify TState implements IMessage
        var agent = new BasicTestAgent();
        var state = agent.GetState();

        // Assert
        state.ShouldNotBeNull();
        state.ShouldBeAssignableTo<IMessage>();
        state.ShouldBeOfType<TestAgentState>();

        await agent.ActivateAsync();

        // Verify it can be serialized/deserialized
        var bytes = state.ToByteArray();
        bytes.ShouldNotBeNull();
        bytes.Length.ShouldBeGreaterThan(0);

        var deserialized = TestAgentState.Parser.ParseFrom(bytes);
        deserialized.ShouldNotBeNull();
    }

    [Fact(DisplayName = "TConfig should be Protobuf message type")]
    public async Task TConfig_Should_Be_Protobuf_Message_Type()
    {
        // Arrange & Act
        var agent = new ConfigurableTestAgent();
        AgentConfigStoreInjector.InjectConfigStore(agent, _serviceProvider);
        var config = new TestAgentConfig
        {
            AgentName = "ProtoTest",
            MaxRetries = 5
        };

        await agent.ConfigAsync(config);

        var actualConfig = agent.GetConfig();

        // Assert
        actualConfig.ShouldNotBeNull();
        actualConfig.ShouldBeAssignableTo<IMessage>();
        actualConfig.ShouldBeOfType<TestAgentConfig>();

        // Verify it can be serialized/deserialized
        var bytes = actualConfig.ToByteArray();
        bytes.ShouldNotBeNull();
        bytes.Length.ShouldBeGreaterThan(0);

        var deserialized = TestAgentConfig.Parser.ParseFrom(bytes);
        deserialized.ShouldNotBeNull();
        deserialized.AgentName.ShouldBe("ProtoTest");
        deserialized.MaxRetries.ShouldBe(5);
    }

    #endregion

    #region Complex Generic Scenarios

    [Fact(DisplayName = "Should handle complex nested generic state")]
    public async Task Should_Handle_Complex_Nested_Generic_State()
    {
        // Arrange
        var agent = new ComplexAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);

        await agent.ActivateAsync();

        // Act - Work with complex nested state
        var state = agent.GetState();
        state.AgentId = "complex-123";
        state.Status = ComplexAgentState.Types.Status.Active;

        // Add nested data
        state.Nested = new ComplexAgentState.Types.NestedState
        {
            SubId = "nested-1",
            SubCounter = 10,
            SubTimestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        for (var i = 0; i < 5; i++)
        {
            state.NestedList.Add(new ComplexAgentState.Types.NestedState
            {
                SubId = $"list-{i}",
                SubCounter = i * 10,
                SubTimestamp = Timestamp.FromDateTime(DateTime.UtcNow.AddMinutes(i))
            });
        }

        state.DataMap["key1"] = "value1";
        state.DataMap["key2"] = "value2";

        // Assert
        state.AgentId.ShouldBe("complex-123");
        state.Status.ShouldBe(ComplexAgentState.Types.Status.Active);
        state.Nested.SubId.ShouldBe("nested-1");
        state.NestedList.Count.ShouldBe(5);
        state.DataMap.Count.ShouldBe(2);

        // Verify serialization works with complex state
        var bytes = state.ToByteArray();
        var deserialized = ComplexAgentState.Parser.ParseFrom(bytes);

        deserialized.AgentId.ShouldBe(state.AgentId);
        deserialized.Status.ShouldBe(state.Status);
        deserialized.Nested.SubId.ShouldBe(state.Nested.SubId);
        deserialized.NestedList.Count.ShouldBe(state.NestedList.Count);
        deserialized.DataMap.Count.ShouldBe(state.DataMap.Count);
    }

    [Fact(DisplayName = "Should support minimal state and config")]
    public async Task Should_Support_Minimal_State_And_Config()
    {
        // Arrange & Act
        var agent = new MinimalGenericAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);

        var config = new MinimalConfig { Enabled = true };

        await agent.ConfigAsync(config);

        await agent.ActivateAsync();

        // Assert - Even minimal types should work
        agent.GetState().ShouldNotBeNull();
        agent.GetState().Value.ShouldBe(0); // Default value

        agent.GetConfig().ShouldNotBeNull();
        agent.GetConfig().Enabled.ShouldBeTrue();

        // Modify and verify
        agent.GetState().Value = 42;
        agent.GetState().Value.ShouldBe(42);
    }

    #endregion
}