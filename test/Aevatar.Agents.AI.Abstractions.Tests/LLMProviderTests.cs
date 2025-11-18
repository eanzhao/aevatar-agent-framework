using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.Core.Tests.Agents.AI;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.AI.Abstractions.Tests;

/// <summary>
/// Tests for IAevatarLLMProvider interface implementations
/// </summary>
public class LLMProviderTests
{
    [Fact]
    public async Task GenerateAsync_ShouldReturnValidResponse()
    {
        // Arrange
        var provider = new MockLLMProvider(
            new AevatarLLMResponse
            {
                Content = "Test response",
                AevatarStopReason = AevatarStopReason.Complete,
                Usage = new AevatarTokenUsage
                {
                    PromptTokens = 20,
                    CompletionTokens = 10,
                    TotalTokens = 30
                }
            });
        
        var request = new AevatarLLMRequest
        {
            SystemPrompt = "You are a test assistant",
            UserPrompt = "Test input"
        };
        
        // Act
        var response = await provider.GenerateAsync(request);
        
        // Assert
        response.ShouldNotBeNull();
        response.Content.ShouldBe("Test response");
        response.AevatarStopReason.ShouldBe(AevatarStopReason.Complete);
        response.Usage.ShouldNotBeNull();
        response.Usage.TotalTokens.ShouldBe(30);
        response.Usage.PromptTokens.ShouldBe(20);
        response.Usage.CompletionTokens.ShouldBe(10);
    }
    
    [Fact]
    public async Task GenerateStreamAsync_ShouldStreamTokens()
    {
        // Arrange
        var provider = new MockLLMProvider(
            new AevatarLLMResponse { Content = "Hello world from stream" });
        
        var request = new AevatarLLMRequest
        {
            SystemPrompt = "System",
            UserPrompt = "User"
        };
        
        // Act
        var tokens = new List<AevatarLLMToken>();
        await foreach (var token in provider.GenerateStreamAsync(request))
        {
            tokens.Add(token);
        }
        
        // Assert
        tokens.ShouldNotBeEmpty();
        tokens.Last().IsComplete.ShouldBeTrue();
        
        var fullContent = string.Join("", tokens.Select(t => t.Content));
        fullContent.ShouldBe("Hello world from stream");
    }
    
    [Fact]
    public async Task GetModelInfoAsync_ShouldReturnInfo()
    {
        // Arrange
        var provider = new MockLLMProvider();
        provider.SetModelInfo(new AevatarModelInfo
        {
            Name = "gpt-4",
            MaxTokens = 8192,
            SupportsStreaming = true,
            SupportsFunctions = true
        });
        
        // Act
        var modelInfo = await provider.GetModelInfoAsync();
        
        // Assert
        modelInfo.ShouldNotBeNull();
        modelInfo.Name.ShouldBe("gpt-4");
        modelInfo.MaxTokens.ShouldBe(8192);
        modelInfo.SupportsStreaming.ShouldBeTrue();
        modelInfo.SupportsFunctions.ShouldBeTrue();
    }
    
    [Fact]
    public async Task GenerateAsync_WithInvalidRequest_ShouldThrow()
    {
        // Arrange
        var provider = new MockLLMProvider();
        provider.ConfigureToThrow(new ArgumentException("Invalid request"));
        
        var request = new AevatarLLMRequest
        {
            SystemPrompt = null,
            UserPrompt = null
        };
        
        // Act & Assert
        var exception = await Should.ThrowAsync<ArgumentException>(
            async () => await provider.GenerateAsync(request));
        
        exception.Message.ShouldContain("Invalid request");
    }
    
    [Fact]
    public async Task GenerateAsync_WithCancellation_ShouldCancel()
    {
        // Arrange
        var provider = new MockLLMProvider();
        var request = new AevatarLLMRequest
        {
            SystemPrompt = "System",
            UserPrompt = "User"
        };
        
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        
        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            async () => await provider.GenerateAsync(request, cts.Token));
    }
    
    [Fact]
    public async Task GenerateAsync_ShouldCaptureRequests()
    {
        // Arrange
        var provider = new MockLLMProvider();
        
        var request1 = new AevatarLLMRequest
        {
            SystemPrompt = "System1",
            UserPrompt = "User1"
        };
        
        var request2 = new AevatarLLMRequest
        {
            SystemPrompt = "System2",
            UserPrompt = "User2"
        };
        
        // Act
        await provider.GenerateAsync(request1);
        await provider.GenerateAsync(request2);
        
        // Assert
        provider.CapturedRequests.Count.ShouldBe(2);
        provider.CapturedRequests[0].SystemPrompt.ShouldBe("System1");
        provider.CapturedRequests[1].UserPrompt.ShouldBe("User2");
    }
    
    [Fact]
    public async Task GenerateStreamAsync_WithCancellation_ShouldStopStreaming()
    {
        // Arrange
        var provider = new MockLLMProvider(
            new AevatarLLMResponse { Content = "This is a very long response that will be streamed" });
        
        var request = new AevatarLLMRequest
        {
            SystemPrompt = "System",
            UserPrompt = "User"
        };
        
        using var cts = new CancellationTokenSource();
        
        // Act
        var tokens = new List<AevatarLLMToken>();
        var enumerator = provider.GenerateStreamAsync(request, cts.Token).GetAsyncEnumerator(cts.Token);
        
        try
        {
            // Get first token
            await enumerator.MoveNextAsync();
            tokens.Add(enumerator.Current);
            
            // Cancel and try to get more
            cts.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(
                async () => await enumerator.MoveNextAsync());
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
        
        // Assert
        tokens.Count.ShouldBe(1);
        tokens[0].IsComplete.ShouldBeFalse();
    }
    
    [Fact]
    public async Task FunctionCallingProvider_ShouldReturnFunctionCall()
    {
        // Arrange
        var provider = new MockFunctionCallingLLMProvider();
        provider.EnqueueFunctionCall(new AevatarFunctionCall
        {
            Name = "get_weather",
            Arguments = "{\"location\": \"Seattle\"}"
        });
        
        var request = new AevatarLLMRequest
        {
            SystemPrompt = "You are a weather assistant",
            UserPrompt = "What's the weather in Seattle?",
            Functions = new List<AevatarFunctionDefinition>
            {
                new()
                {
                    Name = "get_weather",
                    Description = "Get weather information",
                    Parameters = new Dictionary<string, AevatarParameterDefinition>
                    {
                        ["location"] = new() { Type = "string", Required = true }
                    }
                }
            }
        };
        
        // Act
        var response = await provider.GenerateAsync(request);
        
        // Assert
        response.AevatarFunctionCall.ShouldNotBeNull();
        response.AevatarFunctionCall.Name.ShouldBe("get_weather");
        response.AevatarFunctionCall.Arguments.ShouldContain("Seattle");
        response.AevatarStopReason.ShouldBe(AevatarStopReason.AevatarFunctionCall);
    }
}
