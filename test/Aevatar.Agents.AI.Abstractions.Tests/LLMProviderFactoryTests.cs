using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.Core.Tests.Agents.AI;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.AI.Abstractions.Tests;

/// <summary>
/// Tests for ILLMProviderFactory implementations
/// </summary>
public class LLMProviderFactoryTests
{
    [Fact]
    public async Task GetProviderAsync_WithValidName_ShouldReturnProvider()
    {
        // Arrange
        var factory = new MockLLMProviderFactory();
        var mockProvider = new MockLLMProvider();
        factory.RegisterProvider("openai-gpt4", mockProvider);
        
        // Act
        var provider = await factory.GetProviderAsync("openai-gpt4");
        
        // Assert
        provider.ShouldNotBeNull();
        provider.ShouldBe(mockProvider);
    }
    
    [Fact]
    public async Task GetProviderAsync_WithInvalidName_ShouldThrow()
    {
        // Arrange
        var factory = new MockLLMProviderFactory();
        
        // Act & Assert
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await factory.GetProviderAsync("non-existent"));
        
        exception.Message.ShouldContain("not found");
    }
    
    [Fact]
    public void CreateProvider_WithCustomConfig_ShouldWork()
    {
        // Arrange
        var factory = new MockLLMProviderFactory();
        var config = new LLMProviderConfig
        {
            Name = "custom",
            ProviderType = "azure",
            ApiKey = "test-key",
            Model = "gpt-35-turbo",
            Temperature = 0.7,
            MaxTokens = 2000
        };
        
        // Act
        var provider = factory.CreateProvider(config);
        
        // Assert
        provider.ShouldNotBeNull();
        factory.CreatedProviders.ShouldContain(p => p.config.Name == "custom");
    }
    
    [Fact]
    public async Task GetDefaultProviderAsync_ShouldReturnDefault()
    {
        // Arrange
        var factory = new MockLLMProviderFactory();
        var defaultProvider = new MockLLMProvider();
        factory.SetDefaultProvider(defaultProvider);
        
        // Act
        var provider = await factory.GetDefaultProviderAsync();
        
        // Assert
        provider.ShouldNotBeNull();
        provider.ShouldBe(defaultProvider);
    }
    
    [Fact]
    public void GetAvailableProviderNames_ShouldReturnAll()
    {
        // Arrange
        var factory = new MockLLMProviderFactory();
        factory.RegisterProvider("openai-gpt4", new MockLLMProvider());
        factory.RegisterProvider("azure-gpt35", new MockLLMProvider());
        factory.RegisterProvider("claude-3", new MockLLMProvider());
        
        // Act
        var names = factory.GetAvailableProviderNames();
        
        // Assert
        names.ShouldNotBeNull();
        names.Count.ShouldBe(3);
        names.ShouldContain("openai-gpt4");
        names.ShouldContain("azure-gpt35");
        names.ShouldContain("claude-3");
    }
    
    [Fact]
    public void CreateProvider_WithInvalidConfig_ShouldThrow()
    {
        // Arrange
        var factory = new MockLLMProviderFactory();
        factory.ConfigureToThrowOnCreate = true;
        
        var config = new LLMProviderConfig
        {
            ProviderType = null, // Invalid - no provider type
            ApiKey = "test-key"
        };
        
        // Act & Assert
        var exception = Should.Throw<ArgumentException>(
            () => factory.CreateProvider(config));
        
        exception.Message.ShouldContain("Invalid configuration");
    }
    
    [Fact]
    public async Task GetProviderAsync_WithCancellation_ShouldCancel()
    {
        // Arrange
        var factory = new MockLLMProviderFactory();
        factory.DelayMilliseconds = 1000; // Simulate slow operation
        factory.RegisterProvider("slow-provider", new MockLLMProvider());
        
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50); // Cancel after 50ms
        
        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            async () => await factory.GetProviderAsync("slow-provider", cts.Token));
    }
}