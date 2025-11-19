using Shouldly;
using Aevatar.Agents.Core.Tests.Agents;
using Google.Protobuf.WellKnownTypes;
using Aevatar.Agents.Abstractions.Persistence;
using Aevatar.Agents.Core.Helpers;
using Aevatar.Agents.Core.Tests.Fixtures;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.Core.Tests;

/// <summary>
/// GAgentBase Core Tests
/// </summary>
public class GAgentBaseTests(CoreTestFixture fixture) : IClassFixture<CoreTestFixture>
{
    private readonly IServiceProvider _serviceProvider = fixture.ServiceProvider;

    #region State Management Tests

    [Fact(DisplayName = "GAgentBase should initialize state correctly")]
    public void Should_Initialize_State_Correctly()
    {
        // Arrange & Act
        var agent = new BasicTestAgent();
        var state = agent.GetState();

        // Assert
        state.ShouldNotBeNull();
        state.ShouldBeOfType<TestAgentState>();
        state.Counter.ShouldBe(0); // Default value
        state.Name.ShouldBeEmpty(); // Default empty string
        state.Items.ShouldNotBeNull();
        state.Items.ShouldBeEmpty();
    }

    [Fact(DisplayName = "GAgentBase state should serialize with Protobuf")]
    public void Should_Serialize_State_With_Protobuf()
    {
        // Arrange
        var agent = new BasicTestAgent();
        var state = agent.GetState();
        state.Name = "Test";
        state.Counter = 42;
        state.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);

        // Act - Serialize and deserialize
        var bytes = state.ToByteArray();
        var deserializedState = TestAgentState.Parser.ParseFrom(bytes);

        // Assert
        deserializedState.Name.ShouldBe("Test");
        deserializedState.Counter.ShouldBe(42);
        deserializedState.LastUpdated.ShouldNotBeNull();
    }

    [Fact(DisplayName = "GAgentBase should modify and save state")]
    public async Task Should_Modify_And_Save_State()
    {
        // Arrange
        var agent = new BasicTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);

        // Get the injected StateStore from service provider directly
        var stateStore = _serviceProvider.GetRequiredService<IStateStore<TestAgentState>>();

        // Pre-populate the state store with initial data
        var initialState = new TestAgentState { Name = "Initial", Counter = 10 };
        await stateStore.SaveAsync(agent.Id, initialState);

        // Act - Handle an event which should trigger state modification
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(new TestEvent { EventId = "test-1" }),
            PublisherId = "other-agent"
        };

        await agent.HandleEventAsync(envelope);

        // Assert - Verify the state was loaded, modified and saved
        var savedState = await stateStore.LoadAsync(agent.Id);
        savedState.ShouldNotBeNull();
        savedState.Name.ShouldBe("Initial"); // Name should remain unchanged
        savedState.Counter.ShouldBe(11); // Counter should be incremented (assuming handler increments it)

        // Verify we can load the state again and it persists
        var reloadedState = await stateStore.LoadAsync(agent.Id);
        reloadedState.ShouldNotBeNull();
        reloadedState.Counter.ShouldBe(11);
    }

    #endregion

    #region Config Management Tests

    [Fact(DisplayName = "GAgentBase should load existing config")]
    public async Task Should_Load_Existing_Config()
    {
        // Arrange
        var agent = new ConfigurableTestAgent();
        
        // Inject both StateStore and ConfigStore
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        AgentConfigStoreInjector.InjectConfigStore(agent, _serviceProvider);
        
        var configStore = _serviceProvider.GetRequiredService<IConfigStore<TestAgentConfig>>();
    
        var existingConfig = new TestAgentConfig
        {
            AgentName = "PreExistingAgent",
            MaxRetries = 5,
            EnableLogging = false
        };
    
        // Pre-save the configuration
        await configStore.SaveAsync(agent.GetType(), agent.Id, existingConfig);
    
        // Act - Trigger HandleEventAsync which loads config
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(new TestEvent { EventId = "test-config-load" })
        };
        
        await agent.HandleEventAsync(envelope);
    
        // Assert - Config should be loaded from store
        agent.GetConfig().AgentName.ShouldBe("PreExistingAgent");
        agent.GetConfig().MaxRetries.ShouldBe(5);
        agent.GetConfig().EnableLogging.ShouldBe(false);
    }

    [Fact(DisplayName = "GAgentBase should apply custom config")]
    public async Task Should_Apply_Custom_Config()
    {
        // Arrange
        var agent = new ConfigurableTestAgent();

        // Act
        await agent.ActivateAsync();

        // Assert
        var config = agent.GetConfig();
        config.ShouldNotBeNull();
        config.AgentName.ShouldBe("ConfigurableAgent");
        config.MaxRetries.ShouldBe(3);
        config.TimeoutSeconds.ShouldBe(30);
        config.EnableLogging.ShouldBe(true);
    }

    [Fact(DisplayName = "GAgentBase should have default config values")]
    public void Should_Have_Default_Configuration_Values()
    {
        // Arrange & Act
        var agent = new ConfigurableTestAgent();
        var config = agent.GetConfig();

        // Assert
        config.ShouldNotBeNull();
        config.AgentName.ShouldBeEmpty(); // Default protobuf value
        config.MaxRetries.ShouldBe(0); // Default protobuf value
        config.TimeoutSeconds.ShouldBe(0); // Default protobuf value
        config.EnableLogging.ShouldBe(false); // Default protobuf value
    }

    #endregion

    #region Lifecycle Tests

    [Fact(DisplayName = "GAgentBase should activate correctly")]
    public async Task Should_Activate_Correctly()
    {
        // Arrange
        var agent = new BasicTestAgent();

        // Act
        await agent.ActivateAsync();

        // Assert
        agent.OnActivateCalled.ShouldBeTrue();
        agent.OnDeactivateCalled.ShouldBeFalse();
        var state = agent.GetState();
        state.Name.ShouldBe("TestAgent");
        state.Counter.ShouldBe(0);
        state.LastUpdated.ShouldNotBeNull();
    }

    [Fact(DisplayName = "GAgentBase should deactivate correctly")]
    public async Task Should_Deactivate_Correctly()
    {
        // Arrange
        var agent = new BasicTestAgent();
        await agent.ActivateAsync();

        // Act
        await agent.DeactivateAsync();

        // Assert
        agent.OnActivateCalled.ShouldBeTrue();
        agent.OnDeactivateCalled.ShouldBeTrue();
    }

    [Fact(DisplayName = "GAgentBase should handle reactivation")]
    public async Task Should_Handle_Reactivation()
    {
        // Arrange
        var agent = new BasicTestAgent();

        // Act - First activation
        await agent.ActivateAsync();
        var firstState = agent.GetState();

        // Simulate some work
        firstState.Counter = 10;

        // Deactivate
        await agent.DeactivateAsync();

        // Reactivate
        await agent.ActivateAsync();

        // Assert
        agent.OnActivateCalled.ShouldBeTrue();
        agent.OnDeactivateCalled.ShouldBeTrue();
        // State should be reset on reactivation (in this test implementation)
        // Because of BasicTestAgent.OnActivateAsync impl.
        agent.GetState().Counter.ShouldBe(0);
    }

    #endregion

    #region Description Methods Tests

    [Fact(DisplayName = "Should get description synchronously")]
    public void Should_Get_Description_Synchronously()
    {
        // Arrange
        var agent = new BasicTestAgent();
        agent.GetState().Name = "TestName";
        agent.GetState().Counter = 42;

        // Act
        var description = agent.GetDescription();

        // Assert
        description.ShouldNotBeNullOrEmpty();
        description.ShouldContain("TestName");
        description.ShouldContain("42");
    }

    [Fact(DisplayName = "Should get description asynchronously")]
    public async Task Should_Get_Description_Asynchronously()
    {
        // Arrange
        var agent = new BasicTestAgent();
        agent.GetState().Name = "AsyncTest";
        agent.GetState().Counter = 100;

        // Act
        var description = await agent.GetDescriptionAsync();

        // Assert
        description.ShouldNotBeNullOrEmpty();
        description.ShouldContain("AsyncTest");
        description.ShouldContain("100");
    }

    [Fact(DisplayName = "Should provide default description")]
    public void Should_Provide_Default_Description()
    {
        // Arrange & Act
        var agent = new MinimalAgent();
        var description = agent.GetDescription();

        // Assert
        description.ShouldNotBeNullOrEmpty();
        description.ShouldBe(nameof(MinimalAgent));
    }

    [Fact(DisplayName = "Should handle async description errors")]
    public async Task Should_Handle_Async_Description_Errors()
    {
        // Arrange
        var agent = new ErrorDescriptionAgent
        {
            ShouldThrowInGetDescription = true,
            ErrorMessage = "Description generation failed",
            ExceptionType = typeof(InvalidOperationException)
        };
        
        // Act & Assert - InvalidOperationException
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await agent.GetDescriptionAsync()
        );
        exception.Message.ShouldBe("Description generation failed");
        
        // Test with NotImplementedException
        agent.ExceptionType = typeof(NotImplementedException);
        agent.ErrorMessage = "Not implemented yet";
        
        var notImplException = await Should.ThrowAsync<NotImplementedException>(
            async () => await agent.GetDescriptionAsync()
        );
        notImplException.Message.ShouldBe("Not implemented yet");
        
        // Test with TimeoutException
        agent.ExceptionType = typeof(TimeoutException);
        agent.ErrorMessage = "Operation timed out";
        
        var timeoutException = await Should.ThrowAsync<TimeoutException>(
            async () => await agent.GetDescriptionAsync()
        );
        timeoutException.Message.ShouldBe("Operation timed out");
        
        // Test normal operation when not throwing
        agent.ShouldThrowInGetDescription = false;
        var description = await agent.GetDescriptionAsync();
        description.ShouldNotBeNullOrEmpty();
    }

    #endregion

    #region Complex State Tests

    [Fact(DisplayName = "Should handle complex nested state")]
    public async Task Should_Handle_Complex_Nested_State()
    {
        // Arrange
        var agent = new ComplexAgent();

        // Act
        await agent.ActivateAsync();

        // Assert
        var state = agent.GetState();
        state.AgentId.ShouldNotBeNullOrEmpty();
        state.Status.ShouldBe(ComplexAgentState.Types.Status.Active);
        state.Nested.ShouldNotBeNull();
        state.Nested.SubId.ShouldBe("nested-1");
        state.Nested.SubCounter.ShouldBe(0);
    }

    [Fact(DisplayName = "Should serialize complex state with nested messages")]
    public void Should_Serialize_Complex_State()
    {
        // Arrange
        var agent = new ComplexAgent();
        var state = agent.GetState();
        state.AgentId = "complex-1";
        state.Status = ComplexAgentState.Types.Status.Active;
        state.Nested = new ComplexAgentState.Types.NestedState
        {
            SubId = "sub-1",
            SubCounter = 10
        };
        state.NestedList.Add(new ComplexAgentState.Types.NestedState
        {
            SubId = "sub-2",
            SubCounter = 20
        });

        // Act
        var bytes = state.ToByteArray();
        var deserialized = ComplexAgentState.Parser.ParseFrom(bytes);

        // Assert
        deserialized.AgentId.ShouldBe("complex-1");
        deserialized.Status.ShouldBe(ComplexAgentState.Types.Status.Active);
        deserialized.Nested.SubId.ShouldBe("sub-1");
        deserialized.Nested.SubCounter.ShouldBe(10);
        deserialized.NestedList.Count.ShouldBe(1);
        deserialized.NestedList[0].SubId.ShouldBe("sub-2");
        deserialized.NestedList[0].SubCounter.ShouldBe(20);
    }

    #endregion

    #region Generic Support Tests

    [Fact(DisplayName = "Should support single generic parameter")]
    public void Should_Support_Single_Generic_Parameter()
    {
        // Arrange & Act
        var agent = new BasicTestAgent(); // GAgentBase<TState>

        // Assert
        agent.ShouldBeAssignableTo<GAgentBase<TestAgentState>>();
        agent.GetState().ShouldBeOfType<TestAgentState>();
    }

    [Fact(DisplayName = "Should support dual generic parameters")]
    public void Should_Support_Dual_Generic_Parameters()
    {
        // Arrange & Act
        var agent = new ConfigurableTestAgent(); // GAgentBase<TState, TConfig>

        // Assert
        agent.ShouldBeAssignableTo<GAgentBase<TestAgentState, TestAgentConfig>>();
        agent.GetState().ShouldBeOfType<TestAgentState>();
        agent.GetConfig().ShouldBeOfType<TestAgentConfig>();
    }

    [Fact(DisplayName = "TState should be Protobuf message type")]
    public void TState_Should_Be_Protobuf_Message_Type()
    {
        // Arrange
        var agent = new BasicTestAgent();
        var state = agent.GetState();

        // Assert
        state.ShouldBeAssignableTo<IMessage>();
        state.ShouldBeAssignableTo<IBufferMessage>();

        // Should be serializable
        var bytes = state.ToByteArray();
        bytes.ShouldNotBeNull();
        bytes.Length.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact(DisplayName = "TConfig should be Protobuf message type")]
    public void TConfig_Should_Be_Protobuf_Message_Type()
    {
        // Arrange
        var agent = new ConfigurableTestAgent();
        var config = agent.GetConfig();

        // Assert
        config.ShouldBeAssignableTo<IMessage>();
        config.ShouldBeAssignableTo<IBufferMessage>();

        // Should be serializable
        var bytes = config.ToByteArray();
        bytes.ShouldNotBeNull();
        bytes.Length.ShouldBeGreaterThanOrEqualTo(0);
    }

    #endregion
}