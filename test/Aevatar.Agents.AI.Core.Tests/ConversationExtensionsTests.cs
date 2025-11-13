using Aevatar.Agents.AI.Core.Extensions;

namespace Aevatar.Agents.AI.Core.Tests;

/// <summary>
/// Tests for conversation management extension methods.
/// 对话管理扩展方法的测试
/// </summary>
public class ConversationExtensionsTests
{
    private AevatarAIAgentState CreateTestState()
    {
        return new AevatarAIAgentState
        {
            AgentId = "test-agent-" + Guid.NewGuid(),
            AiConfig = new AevatarAIConfiguration
            {
                Model = "gpt-4",
                MaxHistory = 10
            }
        };
    }
    
    [Fact]
    public void AddUserMessage_Should_Add_Message_To_History()
    {
        // Arrange
        var state = CreateTestState();
        var message = "Hello, AI!";
        
        // Act
        state.AddUserMessage(message);
        
        // Assert
        Assert.Single(state.ConversationHistory);
        var addedMessage = state.ConversationHistory[0];
        Assert.Equal(AevatarChatRole.User, addedMessage.Role);
        Assert.Equal(message, addedMessage.Content);
        Assert.NotNull(addedMessage.Id);
        Assert.NotNull(addedMessage.Timestamp);
        Assert.True(addedMessage.TokenCount > 0);
    }
    
    [Fact]
    public void AddAssistantMessage_Should_Add_Message_To_History()
    {
        // Arrange
        var state = CreateTestState();
        var message = "Hello! How can I help you today?";
        
        // Act
        state.AddAssistantMessage(message);
        
        // Assert
        Assert.Single(state.ConversationHistory);
        var addedMessage = state.ConversationHistory[0];
        Assert.Equal(AevatarChatRole.Assistant, addedMessage.Role);
        Assert.Equal(message, addedMessage.Content);
    }
    
    [Fact]
    public void AddSystemMessage_Should_Add_Message_To_History()
    {
        // Arrange
        var state = CreateTestState();
        var message = "You are a helpful assistant.";
        
        // Act
        state.AddSystemMessage(message);
        
        // Assert
        Assert.Single(state.ConversationHistory);
        var addedMessage = state.ConversationHistory[0];
        Assert.Equal(AevatarChatRole.System, addedMessage.Role);
        Assert.Equal(message, addedMessage.Content);
    }
    
    [Fact]
    public void AddFunctionMessage_Should_Add_Message_With_Function_Details()
    {
        // Arrange
        var state = CreateTestState();
        var functionName = "get_weather";
        var result = "Temperature: 72°F, Sunny";
        var arguments = "{\"location\": \"San Francisco\"}";
        
        // Act
        state.AddFunctionMessage(functionName, result, arguments);
        
        // Assert
        Assert.Single(state.ConversationHistory);
        var addedMessage = state.ConversationHistory[0];
        Assert.Equal(AevatarChatRole.Function, addedMessage.Role);
        Assert.Equal(result, addedMessage.Content);
        Assert.Equal(functionName, addedMessage.FunctionName);
        Assert.Equal(arguments, addedMessage.FunctionArguments);
    }
    
    [Fact]
    public void TrimHistory_Should_Keep_Most_Recent_Messages()
    {
        // Arrange
        var state = CreateTestState();
        for (int i = 0; i < 20; i++)
        {
            state.AddUserMessage($"Message {i}");
        }
        
        // Act
        state.TrimHistory(5);
        
        // Assert
        Assert.Equal(5, state.ConversationHistory.Count);
        Assert.Equal("Message 15", state.ConversationHistory[0].Content);
        Assert.Equal("Message 19", state.ConversationHistory[4].Content);
    }
    
    [Fact]
    public void TrimToTokenLimit_Should_Remove_Oldest_Non_System_Messages()
    {
        // Arrange
        var state = CreateTestState();
        state.AddSystemMessage("System prompt");
        
        // Add many messages to exceed token limit
        for (int i = 0; i < 100; i++)
        {
            state.AddUserMessage($"User message {i}");
            state.AddAssistantMessage($"Assistant response {i}");
        }
        
        // Act - Keep only 100 tokens (approximately 25 characters)
        state.TrimToTokenLimit(100, preserveSystemMessage: true);
        
        // Assert
        Assert.True(state.ConversationHistory.Count > 0);
        Assert.Contains(state.ConversationHistory, m => m.Role == AevatarChatRole.System);
        Assert.True(state.GetEstimatedTokenCount() <= 100);
    }
    
    [Fact]
    public void GetRecentHistory_Should_Return_Limited_Messages()
    {
        // Arrange
        var state = CreateTestState();
        for (int i = 0; i < 10; i++)
        {
            state.AddUserMessage($"Message {i}");
        }
        
        // Act
        var recent = state.GetRecentHistory(3);
        
        // Assert
        Assert.Equal(3, recent.Count);
        Assert.Equal("Message 7", recent[0].Content);
        Assert.Equal("Message 9", recent[2].Content);
    }
    
    [Fact]
    public void GetMessagesByRole_Should_Filter_Correctly()
    {
        // Arrange
        var state = CreateTestState();
        state.AddSystemMessage("System");
        state.AddUserMessage("User 1");
        state.AddAssistantMessage("Assistant 1");
        state.AddUserMessage("User 2");
        state.AddAssistantMessage("Assistant 2");
        
        // Act
        var userMessages = state.GetMessagesByRole(AevatarChatRole.User);
        var assistantMessages = state.GetMessagesByRole(AevatarChatRole.Assistant);
        
        // Assert
        Assert.Equal(2, userMessages.Count);
        Assert.Equal(2, assistantMessages.Count);
        Assert.All(userMessages, m => Assert.Equal(AevatarChatRole.User, m.Role));
        Assert.All(assistantMessages, m => Assert.Equal(AevatarChatRole.Assistant, m.Role));
    }
    
    [Fact]
    public void GetLastMessage_Should_Return_Most_Recent()
    {
        // Arrange
        var state = CreateTestState();
        state.AddUserMessage("First");
        state.AddAssistantMessage("Second");
        state.AddUserMessage("Third");
        
        // Act
        var last = state.GetLastMessage();
        
        // Assert
        Assert.NotNull(last);
        Assert.Equal("Third", last.Content);
    }
    
    [Fact]
    public void GetLastUserMessage_Should_Return_Most_Recent_User_Message()
    {
        // Arrange
        var state = CreateTestState();
        state.AddUserMessage("User 1");
        state.AddAssistantMessage("Assistant 1");
        state.AddUserMessage("User 2");
        state.AddAssistantMessage("Assistant 2");
        
        // Act
        var lastUser = state.GetLastUserMessage();
        
        // Assert
        Assert.NotNull(lastUser);
        Assert.Equal("User 2", lastUser.Content);
    }
    
    [Fact]
    public void ClearConversationHistory_Should_Remove_All_Messages()
    {
        // Arrange
        var state = CreateTestState();
        for (int i = 0; i < 5; i++)
        {
            state.AddUserMessage($"Message {i}");
        }
        
        // Act
        state.ClearConversationHistory();
        
        // Assert
        Assert.Empty(state.ConversationHistory);
    }
    
    [Fact]
    public void GetEstimatedTokenCount_Should_Calculate_Total_Tokens()
    {
        // Arrange
        var state = CreateTestState();
        state.AddUserMessage("Hello");  // ~2 tokens
        state.AddAssistantMessage("Hi there!");  // ~3 tokens
        
        // Act
        var totalTokens = state.GetEstimatedTokenCount();
        
        // Assert
        Assert.True(totalTokens > 0);
        Assert.Equal(state.ConversationHistory.Sum(m => m.TokenCount), totalTokens);
    }
    
    [Fact]
    public void ExportConversationAsJson_Should_Generate_Valid_Json()
    {
        // Arrange
        var state = CreateTestState();
        state.AddUserMessage("Hello");
        state.AddAssistantMessage("Hi!");
        
        // Act
        var json = state.ExportConversationAsJson();
        
        // Assert
        Assert.NotNull(json);
        Assert.Contains("\"role\" : \"User\",", json);
        Assert.Contains("\"role\" : \"Assistant\"", json);
        Assert.Contains("\"content\" : \"Hello\"", json);
        Assert.Contains("\"content\" : \"Hi!\"", json);
    }
    
    [Fact]
    public void ExportConversationAsMarkdown_Should_Generate_Valid_Markdown()
    {
        // Arrange
        var state = CreateTestState();
        state.AddUserMessage("Hello");
        state.AddAssistantMessage("Hi!");
        state.AddFunctionMessage("get_time", "12:00 PM", null);
        
        // Act
        var markdown = state.ExportConversationAsMarkdown();
        
        // Assert
        Assert.NotNull(markdown);
        Assert.Contains("# Conversation History", markdown);
        Assert.Contains("## 👤 User", markdown);
        Assert.Contains("## 🤖 Assistant", markdown);
        Assert.Contains("## 🔧 Function: get_time", markdown);
        Assert.Contains("Hello", markdown);
        Assert.Contains("Hi!", markdown);
        Assert.Contains("12:00 PM", markdown);
    }
    
    [Fact]
    public void GetConversationSummary_Should_Include_Statistics()
    {
        // Arrange
        var state = CreateTestState();
        state.AddSystemMessage("System");
        state.AddUserMessage("User");
        state.AddAssistantMessage("Assistant");
        state.AddFunctionMessage("func", "result", null);
        
        // Act
        var summary = state.GetConversationSummary();
        
        // Assert
        Assert.NotNull(summary);
        Assert.Contains("4 messages", summary);
        Assert.Contains("User messages: 1", summary);
        Assert.Contains("Assistant messages: 1", summary);
        Assert.Contains("System messages: 1", summary);
        Assert.Contains("Function messages: 1", summary);
        Assert.Contains("Total tokens:", summary);
    }
    
    [Fact]
    public void CreateConversationSummary_Should_Generate_Summary_Object()
    {
        // Arrange
        var state = CreateTestState();
        state.AddUserMessage("What's the weather like?");
        state.AddAssistantMessage("It's sunny and 72°F today.");
        state.AddUserMessage("Perfect for a walk!");
        state.AddAssistantMessage("Yes, it's great weather for outdoor activities.");
        
        // Act
        var summary = state.CreateConversationSummary(4);
        
        // Assert
        Assert.NotNull(summary);
        Assert.NotNull(summary.Content);
        Assert.Contains("4 messages", summary.Content);
        Assert.NotEmpty(summary.Sentiment);
    }
    
    [Fact]
    public void MaxHistory_Should_Limit_Message_Count()
    {
        // Arrange
        var state = CreateTestState();
        var maxHistory = 5;
        
        // Act - Add more messages than maxHistory
        for (int i = 0; i < 10; i++)
        {
            state.AddUserMessage($"Message {i}", maxHistory);
        }
        
        // Assert
        Assert.Equal(maxHistory, state.ConversationHistory.Count);
        // Should keep most recent messages
        Assert.Equal("Message 5", state.ConversationHistory[0].Content);
        Assert.Equal("Message 9", state.ConversationHistory[4].Content);
    }
    
    [Fact]
    public void TokenEstimation_Should_Be_Reasonable()
    {
        // Arrange & Act
        var state = CreateTestState();
        
        // Test various message lengths
        state.AddUserMessage("Hi");  // ~1 token
        state.AddUserMessage("Hello there");  // ~2-3 tokens
        state.AddUserMessage("How are you doing today?");  // ~5-6 tokens
        
        var totalTokens = state.GetEstimatedTokenCount();
        
        // Assert - Very rough estimation, but should be in reasonable range
        Assert.InRange(totalTokens, 5, 20);
    }
}

