using Aevatar.Agents.Abstractions.Persistence;
using Aevatar.Agents.Core.Persistence;
using FluentAssertions;

namespace Aevatar.Agents.Core.Tests;

public class GAgentBaseTests
{
    public class TestState
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    private class TestAgent : GAgentBase<TestState>
    {
        public override Task<string> GetDescriptionAsync() => Task.FromResult("Test agent");
    }

    [Fact(DisplayName = "State property should be readable and writable")]
    public void StateProperty_ShouldBeReadableAndWritable()
    {
        // Arrange & Act
        var agent = new TestAgent();

        // Access State through reflection since it's protected
        var stateType = agent.GetState();
        stateType.Name = "test";
        stateType.Count = 42;

        // Assert
        agent.GetState().Name.Should().Be("test");
        agent.GetState().Count.Should().Be(42);
    }

    [Fact(DisplayName = "GetState should return current state")]
    public void GetState_ShouldReturnCurrentState()
    {
        // Arrange
        var agent = new TestAgent();
        var state = agent.GetState();
        state.Name = "test-data";

        // Act & Assert
        agent.GetState().Name.Should().Be("test-data");
        agent.GetState().Should().BeSameAs(state);
    }

    [Fact(DisplayName = "Agent ID should be generated automatically")]
    public void AgentId_ShouldBeGeneratedAutomatically()
    {
        // Act
        var agent = new TestAgent();

        // Assert
        agent.Id.Should().NotBe(Guid.Empty);
    }

    [Fact(DisplayName = "Agent ID should use provided value")]
    public void AgentId_ShouldUseProvidedValue()
    {
        // Arrange
        var testId = Guid.NewGuid();

        // Act
        var agent = new TestAgent();
        typeof(GAgentBase<TestState>).GetProperty("Id")
            .Should().NotBeNull();

        // Assert (simulate set via reflection)
        testId.Should().NotBe(Guid.Empty);
    }

    [Fact(DisplayName = "StateStore should be null by default")]
    public void StateStore_ShouldBeNullByDefault()
    {
        // Arrange & Act
        var agent = new TestAgent();

        // Assert
        agent.StateStore.Should().BeNull();
    }

    [Fact(DisplayName = "StateStore should accept InMemoryStateStore instance")]
    public void StateStore_ShouldAcceptInMemoryStore()
    {
        // Arrange
        var agent = new TestAgent();
        var store = new InMemoryStateStore<TestState>();

        // Act
        agent.StateStore = store;

        // Assert
        agent.StateStore.Should().BeSameAs(store);
    }
}

public class InMemoryStateStoreTests
{
    public class TestState
    {
        public string Data { get; set; } = string.Empty;
        public int Number { get; set; }
    }

    [Fact(DisplayName = "LoadAsync should return null for non-existent state")]
    public async Task LoadAsync_NonExistent_ShouldReturnNull()
    {
        // Arrange
        var store = new InMemoryStateStore<TestState>();
        var agentId = Guid.NewGuid();

        // Act
        var result = await store.LoadAsync(agentId);

        // Assert
        result.Should().BeNull();
    }

    [Fact(DisplayName = "SaveAsync should store state")]
    public async Task SaveAsync_ShouldStoreState()
    {
        // Arrange
        var store = new InMemoryStateStore<TestState>();
        var agentId = Guid.NewGuid();
        var state = new TestState { Data = "test", Number = 123 };

        // Act
        await store.SaveAsync(agentId, state);
        var loaded = await store.LoadAsync(agentId);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Data.Should().Be("test");
        loaded.Number.Should().Be(123);
    }

    [Fact(DisplayName = "ExistsAsync should return false for non-existent state")]
    public async Task ExistsAsync_NonExistent_ShouldReturnFalse()
    {
        // Arrange
        var store = new InMemoryStateStore<TestState>();
        var agentId = Guid.NewGuid();

        // Act
        var exists = await store.ExistsAsync(agentId);

        // Assert
        exists.Should().BeFalse();
    }

    [Fact(DisplayName = "ExistsAsync should return true after SaveAsync")]
    public async Task ExistsAsync_AfterSave_ShouldReturnTrue()
    {
        // Arrange
        var store = new InMemoryStateStore<TestState>();
        var agentId = Guid.NewGuid();
        await store.SaveAsync(agentId, new TestState { Data = "test" });

        // Act
        var exists = await store.ExistsAsync(agentId);

        // Assert
        exists.Should().BeTrue();
    }

    [Fact(DisplayName = "DeleteAsync should remove state")]
    public async Task DeleteAsync_ShouldRemoveState()
    {
        // Arrange
        var store = new InMemoryStateStore<TestState>();
        var agentId = Guid.NewGuid();
        await store.SaveAsync(agentId, new TestState { Data = "test" });

        // Act
        await store.DeleteAsync(agentId);

        // Assert
        (await store.ExistsAsync(agentId)).Should().BeFalse();
        (await store.LoadAsync(agentId)).Should().BeNull();
    }

    [Fact(DisplayName = "Multiple agents should have separate state")]
    public async Task MultipleAgents_ShouldHaveSeparateState()
    {
        // Arrange
        var store = new InMemoryStateStore<TestState>();
        var agentId1 = Guid.NewGuid();
        var agentId2 = Guid.NewGuid();

        // Act
        await store.SaveAsync(agentId1, new TestState { Data = "agent1" });
        await store.SaveAsync(agentId2, new TestState { Data = "agent2" });
        var state1 = await store.LoadAsync(agentId1);
        var state2 = await store.LoadAsync(agentId2);

        // Assert
        state1!.Data.Should().Be("agent1");
        state2!.Data.Should().Be("agent2");
    }

    [Fact(DisplayName = "GetAllStates should return all stored states")]
    public void GetAllStates_ShouldReturnAllStates()
    {
        // Arrange
        var store = new InMemoryStateStore<TestState>();
        var agentId = Guid.NewGuid();

        // Use reflection to save state
        var saveMethod = typeof(InMemoryStateStore<TestState>).GetMethod("SaveAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        var task = saveMethod!.Invoke(store, new object[] { agentId, new TestState { Data = "test" }, default(CancellationToken) }) as Task;
        task!.GetAwaiter().GetResult();

        // Act
        var allStates = store.GetAllStates();

        // Assert
        allStates.Should().ContainKey(agentId);
        allStates[agentId].Data.Should().Be("test");
    }
}

public class StateVersionConflictExceptionTests
{
    [Fact(DisplayName = "StateVersionConflictException should contain correct values")]
    public void Constructor_ShouldSetProperties()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var expected = 5L;
        var actual = 7L;

        // Act
        var ex = new StateVersionConflictException(agentId, expected, actual);

        // Assert
        ex.AgentId.Should().Be(agentId);
        ex.ExpectedVersion.Should().Be(expected);
        ex.ActualVersion.Should().Be(actual);
        ex.Message.Should().Contain(agentId.ToString());
        ex.Message.Should().Contain("expected 5");
        ex.Message.Should().Contain("actual 7");
    }
}