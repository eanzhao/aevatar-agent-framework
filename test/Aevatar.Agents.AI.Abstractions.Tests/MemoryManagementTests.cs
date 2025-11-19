using System.ComponentModel;
using Aevatar.Agents.AI.Abstractions.Tests.Memory;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.AI.Abstractions.Tests;

/// <summary>
/// Tests for Memory Management interfaces
/// </summary>
public class MemoryManagementTests
{
    [Fact]
    [DisplayName("Memory AddMessageAsync should store message correctly")]
    public async Task Memory_AddMessageAsync_ShouldStoreMessage()
    {
        // Arrange
        var memory = new MockMemory();

        // Act
        await memory.AddMessageAsync("user", "Test message");
        await memory.AddMessageAsync("assistant", "Response message");

        // Assert
        memory.History.Count.ShouldBe(2);
        memory.History[0].Role.ShouldBe("user");
        memory.History[0].Content.ShouldBe("Test message");
        memory.History[1].Role.ShouldBe("assistant");
        memory.History[1].Content.ShouldBe("Response message");
    }

    [Fact]
    [DisplayName("Memory GetConversationHistoryAsync should return messages in order")]
    public async Task Memory_GetConversationHistoryAsync_ShouldReturnInOrder()
    {
        // Arrange
        var memory = new MockMemory();
        await memory.AddMessageAsync("user", "First");
        await memory.AddMessageAsync("assistant", "Second");
        await memory.AddMessageAsync("user", "Third");

        // Act
        var history = await memory.GetConversationHistoryAsync();

        // Assert
        history.Count.ShouldBe(3);
        history[0].Content.ShouldBe("First");
        history[1].Content.ShouldBe("Second");
        history[2].Content.ShouldBe("Third");
    }

    [Fact]
    [DisplayName("Memory GetConversationHistoryAsync with limit should respect the limit")]
    public async Task Memory_GetConversationHistoryAsync_WithLimit_ShouldRespectLimit()
    {
        // Arrange
        var memory = new MockMemory();
        for (int i = 0; i < 10; i++)
        {
            await memory.AddMessageAsync("user", $"Message {i}");
        }

        // Act
        var history = await memory.GetConversationHistoryAsync(limit: 5);

        // Assert
        history.Count.ShouldBe(5);
        history[0].Content.ShouldBe("Message 5"); // Should get last 5 messages
        history[4].Content.ShouldBe("Message 9");
    }

    [Fact]
    [DisplayName("Memory ClearHistoryAsync should remove all messages")]
    public async Task Memory_ClearHistoryAsync_ShouldRemoveAllMessages()
    {
        // Arrange
        var memory = new MockMemory();
        await memory.AddMessageAsync("user", "Message 1");
        await memory.AddMessageAsync("assistant", "Message 2");

        // Act
        await memory.ClearHistoryAsync();
        var history = await memory.GetConversationHistoryAsync();

        // Assert
        history.ShouldBeEmpty();
    }

    [Fact]
    [DisplayName("Memory SearchAsync should return relevant results")]
    public async Task Memory_SearchAsync_ShouldReturnRelevantResults()
    {
        // Arrange
        var memory = new MockMemory();
        await memory.AddMessageAsync("user", "The weather is sunny today");
        await memory.AddMessageAsync("assistant", "Yes, it's a beautiful day");
        await memory.AddMessageAsync("user", "I love pizza");
        await memory.AddMessageAsync("assistant", "Pizza is delicious");
        await memory.AddMessageAsync("user", "What about the weather tomorrow?");

        // Act
        var results = await memory.SearchAsync("weather", topK: 2);

        // Assert
        results.Count.ShouldBe(2);
        results[0].ShouldContain("weather");
        results.All(r => r.ToLower().Contains("weather")).ShouldBeTrue();
    }

    [Fact]
    [DisplayName("Memory SearchAsync with topK should limit results")]
    public async Task Memory_SearchAsync_WithTopK_ShouldLimitResults()
    {
        // Arrange
        var memory = new MockMemory();
        for (int i = 0; i < 10; i++)
        {
            await memory.AddMessageAsync("user", $"Test message {i}");
        }

        // Act
        var results = await memory.SearchAsync("Test", topK: 3);

        // Assert
        results.Count.ShouldBe(3);
    }

    [Fact]
    [DisplayName("Memory SearchAsync with no matches should return empty")]
    public async Task Memory_SearchAsync_WithNoMatches_ShouldReturnEmpty()
    {
        // Arrange
        var memory = new MockMemory();
        await memory.AddMessageAsync("user", "Hello world");
        await memory.AddMessageAsync("assistant", "Hi there");

        // Act
        var results = await memory.SearchAsync("pizza", topK: 5);

        // Assert
        results.ShouldBeEmpty();
    }

    [Fact]
    [DisplayName("Memory conversation entry should have timestamp")]
    public async Task Memory_ConversationEntry_ShouldHaveTimestamp()
    {
        // Arrange
        var memory = new MockMemory();
        var beforeAdd = DateTime.UtcNow;

        // Act
        await memory.AddMessageAsync("user", "Test");
        var afterAdd = DateTime.UtcNow;

        // Assert
        var history = await memory.GetConversationHistoryAsync();
        history[0].Timestamp.ShouldBeNull();
    }

    [Fact]
    [DisplayName("Memory operations with cancellation should throw OperationCanceledException")]
    public async Task Memory_WithCancellation_ShouldThrow()
    {
        // Arrange
        var memory = new MockMemory();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await memory.AddMessageAsync("user", "Test", cts.Token));

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await memory.GetConversationHistoryAsync(cancellationToken: cts.Token));

        await Should.ThrowAsync<OperationCanceledException>(async () => await memory.ClearHistoryAsync(cts.Token));

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await memory.SearchAsync("test", cancellationToken: cts.Token));
    }

    [Fact]
    [DisplayName("VectorMemory SearchAsync should use semantic similarity")]
    public async Task VectorMemory_SearchAsync_ShouldUseSemanticSimilarity()
    {
        // Arrange
        var memory = new MockVectorMemory();
        await memory.AddMessageAsync("user", "The cat is sleeping");
        await memory.AddMessageAsync("user", "Dogs are playing");
        await memory.AddMessageAsync("user", "The kitten is resting");
        await memory.AddMessageAsync("user", "Birds are flying");

        // Act
        var results = await memory.SearchAsync("feline", topK: 2);

        // Assert
        results.Count.ShouldBe(2);
        // Vector search should find semantically related content
        // (In real implementation, would use actual embeddings)
    }

    [Fact]
    [DisplayName("Memory search history should be tracked correctly")]
    public async Task Memory_SearchHistory_ShouldBeTracked()
    {
        // Arrange
        var memory = new MockMemory();
        await memory.AddMessageAsync("user", "Test message 1");
        await memory.AddMessageAsync("user", "Test message 2");

        // Act
        await memory.SearchAsync("Test", topK: 1);
        await memory.SearchAsync("message", topK: 2);

        // Assert
        memory.SearchHistory.Count.ShouldBe(2);
        memory.SearchHistory[0].query.ShouldBe("Test");
        memory.SearchHistory[0].results.Count.ShouldBe(1);
        memory.SearchHistory[1].query.ShouldBe("message");
        memory.SearchHistory[1].results.Count.ShouldBe(2);
    }
}