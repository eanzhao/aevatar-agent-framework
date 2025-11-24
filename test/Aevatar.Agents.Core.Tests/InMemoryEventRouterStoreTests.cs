using Aevatar.Agents.Core.EventRouting;
using FluentAssertions;

namespace Aevatar.Agents.Core.Tests;

public class InMemoryEventRouterStoreTests
{
    [Fact(DisplayName = "SaveAsync and LoadAsync should persist and retrieve hierarchy")]
    public async Task SaveAndLoad_ShouldPersistHierarchy()
    {
        // Arrange
        var store = new InMemoryEventRouterStore();
        var agentId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var child1Id = Guid.NewGuid();
        var child2Id = Guid.NewGuid();

        var hierarchy = new Aevatar.Agents.Abstractions.EventRouting.EventRouterHierarchy
        {
            ParentId = parentId,
            ChildrenIds = new HashSet<Guid> { child1Id, child2Id }
        };

        // Act
        await store.SaveAsync(agentId, hierarchy);
        var loaded = await store.LoadAsync(agentId);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.ParentId.Should().Be(parentId);
        loaded.ChildrenIds.Should().BeEquivalentTo(new[] { child1Id, child2Id });
    }

    [Fact(DisplayName = "LoadAsync should return null for non-existent agent")]
    public async Task LoadAsync_ShouldReturnNullForNonExistentAgent()
    {
        // Arrange
        var store = new InMemoryEventRouterStore();
        var agentId = Guid.NewGuid();

        // Act
        var result = await store.LoadAsync(agentId);

        // Assert
        result.Should().BeNull();
    }

    [Fact(DisplayName = "SaveAsync should overwrite existing hierarchy")]
    public async Task SaveAsync_ShouldOverwriteExistingHierarchy()
    {
        // Arrange
        var store = new InMemoryEventRouterStore();
        var agentId = Guid.NewGuid();
        var parent1 = Guid.NewGuid();
        var parent2 = Guid.NewGuid();

        var hierarchy1 = new Aevatar.Agents.Abstractions.EventRouting.EventRouterHierarchy
        {
            ParentId = parent1,
            ChildrenIds = new HashSet<Guid>()
        };

        var hierarchy2 = new Aevatar.Agents.Abstractions.EventRouting.EventRouterHierarchy
        {
            ParentId = parent2,
            ChildrenIds = new HashSet<Guid>()
        };

        // Act
        await store.SaveAsync(agentId, hierarchy1);
        await store.SaveAsync(agentId, hierarchy2);
        var loaded = await store.LoadAsync(agentId);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.ParentId.Should().Be(parent2);
    }

    [Fact(DisplayName = "DeleteAsync should remove hierarchy")]
    public async Task DeleteAsync_ShouldRemoveHierarchy()
    {
        // Arrange
        var store = new InMemoryEventRouterStore();
        var agentId = Guid.NewGuid();
        var hierarchy = new Aevatar.Agents.Abstractions.EventRouting.EventRouterHierarchy
        {
            ParentId = Guid.NewGuid(),
            ChildrenIds = new HashSet<Guid>()
        };

        await store.SaveAsync(agentId, hierarchy);

        // Act
        await store.DeleteAsync(agentId);
        var loaded = await store.LoadAsync(agentId);

        // Assert
        loaded.Should().BeNull();
    }

    [Fact(DisplayName = "ExistsAsync should return true for existing hierarchy")]
    public async Task ExistsAsync_ShouldReturnTrueForExistingHierarchy()
    {
        // Arrange
        var store = new InMemoryEventRouterStore();
        var agentId = Guid.NewGuid();
        var hierarchy = new Aevatar.Agents.Abstractions.EventRouting.EventRouterHierarchy
        {
            ParentId = null,
            ChildrenIds = new HashSet<Guid>()
        };

        await store.SaveAsync(agentId, hierarchy);

        // Act
        var exists = await store.ExistsAsync(agentId);

        // Assert
        exists.Should().BeTrue();
    }

    [Fact(DisplayName = "ExistsAsync should return false for non-existent hierarchy")]
    public async Task ExistsAsync_ShouldReturnFalseForNonExistentHierarchy()
    {
        // Arrange
        var store = new InMemoryEventRouterStore();
        var agentId = Guid.NewGuid();

        // Act
        var exists = await store.ExistsAsync(agentId);

        // Assert
        exists.Should().BeFalse();
    }

    [Fact(DisplayName = "Multiple agents should be isolated")]
    public async Task MultipleAgents_ShouldBeIsolated()
    {
        // Arrange
        var store = new InMemoryEventRouterStore();
        var agent1Id = Guid.NewGuid();
        var agent2Id = Guid.NewGuid();
        var parent1 = Guid.NewGuid();
        var parent2 = Guid.NewGuid();

        var hierarchy1 = new Aevatar.Agents.Abstractions.EventRouting.EventRouterHierarchy
        {
            ParentId = parent1,
            ChildrenIds = new HashSet<Guid>()
        };

        var hierarchy2 = new Aevatar.Agents.Abstractions.EventRouting.EventRouterHierarchy
        {
            ParentId = parent2,
            ChildrenIds = new HashSet<Guid>()
        };

        // Act
        await store.SaveAsync(agent1Id, hierarchy1);
        await store.SaveAsync(agent2Id, hierarchy2);

        var loaded1 = await store.LoadAsync(agent1Id);
        var loaded2 = await store.LoadAsync(agent2Id);

        // Assert
        loaded1!.ParentId.Should().Be(parent1);
        loaded2!.ParentId.Should().Be(parent2);
    }

    [Fact(DisplayName = "Parallel operations should be thread-safe")]
    public async Task ParallelOperations_ShouldBeThreadSafe()
    {
        // Arrange
        var store = new InMemoryEventRouterStore();
        var tasks = new List<Task>();

        // Act - Perform 100 parallel save operations
        for (int i = 0; i < 100; i++)
        {
            var agentId = Guid.NewGuid();
            var hierarchy = new Aevatar.Agents.Abstractions.EventRouting.EventRouterHierarchy
            {
                ParentId = Guid.NewGuid(),
                ChildrenIds = new HashSet<Guid> { Guid.NewGuid() }
            };

            tasks.Add(store.SaveAsync(agentId, hierarchy));
        }

        await Task.WhenAll(tasks);

        // Assert - All operations should complete without exception
        var allHierarchies = store.GetAllHierarchies();
        allHierarchies.Should().HaveCount(100);
    }

    [Fact(DisplayName = "Clear should remove all hierarchies")]
    public async Task Clear_ShouldRemoveAllHierarchies()
    {
        // Arrange
        var store = new InMemoryEventRouterStore();
        await store.SaveAsync(Guid.NewGuid(), new Aevatar.Agents.Abstractions.EventRouting.EventRouterHierarchy());
        await store.SaveAsync(Guid.NewGuid(), new Aevatar.Agents.Abstractions.EventRouting.EventRouterHierarchy());

        // Act
        store.Clear();

        // Assert
        store.GetAllHierarchies().Should().BeEmpty();
    }
}
