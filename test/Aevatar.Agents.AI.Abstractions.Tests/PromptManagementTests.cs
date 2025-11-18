using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.AI.Abstractions;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.AI.Abstractions.Tests;

/// <summary>
/// Tests for Prompt Management interfaces
/// </summary>
public class PromptManagementTests
{
    [Fact]
    public async Task PromptManager_GetSystemPromptAsync_WithKey_ShouldReturnCorrectPrompt()
    {
        // Arrange
        var manager = new MockPromptManager();
        manager.AddSystemPrompt("customer-service", "You are a helpful customer service agent.");
        manager.AddSystemPrompt("technical-support", "You are a technical support specialist.");
        
        // Act
        var prompt1 = await manager.GetSystemPromptAsync("customer-service");
        var prompt2 = await manager.GetSystemPromptAsync("technical-support");
        
        // Assert
        prompt1.ShouldContain("customer service");
        prompt2.ShouldContain("technical support");
    }
    
    [Fact]
    public async Task PromptManager_GetSystemPromptAsync_WithInvalidKey_ShouldReturnDefault()
    {
        // Arrange
        var manager = new MockPromptManager();
        manager.SetDefaultPrompt("You are a helpful AI assistant.");
        
        // Act
        var prompt = await manager.GetSystemPromptAsync("non-existent-key");
        
        // Assert
        prompt.ShouldBe("You are a helpful AI assistant.");
    }
    
    [Fact]
    public async Task PromptManager_FormatPromptAsync_ShouldReplaceVariables()
    {
        // Arrange
        var manager = new MockPromptManager();
        var template = "Hello {{name}}, your order #{{orderId}} is {{status}}.";
        var variables = new Dictionary<string, object>
        {
            ["name"] = "Alice",
            ["orderId"] = "12345",
            ["status"] = "ready for pickup"
        };
        
        // Act
        var formatted = await manager.FormatPromptAsync(template, variables);
        
        // Assert
        formatted.ShouldBe("Hello Alice, your order #12345 is ready for pickup.");
    }
    
    [Fact]
    public async Task PromptManager_FormatPromptAsync_WithMissingVariables_ShouldHandleGracefully()
    {
        // Arrange
        var manager = new MockPromptManager();
        var template = "Hello {{name}}, your balance is {{balance}}.";
        var variables = new Dictionary<string, object>
        {
            ["name"] = "Bob"
            // Missing 'balance' variable
        };
        
        // Act
        var formatted = await manager.FormatPromptAsync(template, variables);
        
        // Assert
        formatted.ShouldBe("Hello Bob, your balance is {{balance}}.");
        // Or could be configured to use a default value
    }
    
    [Fact]
    public async Task PromptManager_FormatPromptAsync_WithNestedVariables_ShouldWork()
    {
        // Arrange
        var manager = new MockPromptManager();
        var template = "User: {{user.name}} ({{user.email}})";
        var variables = new Dictionary<string, object>
        {
            ["user.name"] = "Charlie",
            ["user.email"] = "charlie@example.com"
        };
        
        // Act
        var formatted = await manager.FormatPromptAsync(template, variables);
        
        // Assert
        formatted.ShouldBe("User: Charlie (charlie@example.com)");
    }
    
    [Fact]
    public async Task PromptManager_BuildChatPromptAsync_ShouldMaintainMessageOrder()
    {
        // Arrange
        var manager = new MockPromptManager();
        var systemPrompt = "You are a helpful assistant.";
        var history = new List<AevatarChatMessage>
        {
            new() { Role = AevatarChatRole.User, Content = "Hello" },
            new() { Role = AevatarChatRole.Assistant, Content = "Hi there!" },
            new() { Role = AevatarChatRole.User, Content = "How are you?" }
        };
        
        // Act
        var messages = await manager.BuildChatPromptAsync(systemPrompt, history);
        
        // Assert
        messages.Count.ShouldBe(4); // System + 3 history messages
        messages[0].Role.ShouldBe(AevatarChatRole.System);
        messages[0].Content.ShouldBe(systemPrompt);
        messages[1].Role.ShouldBe(AevatarChatRole.User);
        messages[1].Content.ShouldBe("Hello");
        messages[2].Role.ShouldBe(AevatarChatRole.Assistant);
        messages[3].Role.ShouldBe(AevatarChatRole.User);
    }
    
    [Fact]
    public async Task PromptManager_BuildChatPromptAsync_WithNoHistory_ShouldOnlyHaveSystem()
    {
        // Arrange
        var manager = new MockPromptManager();
        var systemPrompt = "System prompt";
        
        // Act
        var messages = await manager.BuildChatPromptAsync(systemPrompt);
        
        // Assert
        messages.Count.ShouldBe(1);
        messages[0].Role.ShouldBe(AevatarChatRole.System);
        messages[0].Content.ShouldBe(systemPrompt);
    }
    
    [Fact]
    public async Task PromptManager_WithCancellation_ShouldThrow()
    {
        // Arrange
        var manager = new MockPromptManager();
        manager.SimulateDelay = true;
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        
        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            async () => await manager.GetSystemPromptAsync("test", cts.Token));
        
        await Should.ThrowAsync<OperationCanceledException>(
            async () => await manager.FormatPromptAsync("test", null, cts.Token));
        
        await Should.ThrowAsync<OperationCanceledException>(
            async () => await manager.BuildChatPromptAsync("test", null, cts.Token));
    }
    
    [Fact]
    public async Task PromptTemplate_Validation_ShouldDetectInvalidTemplates()
    {
        // Arrange
        var template = new AevatarPromptTemplate
        {
            Name = "test-template",
            Content = "Hello {{name}}, {{greeting",  // Invalid - unclosed variable
            Parameters = new Dictionary<string, AevatarTemplateParameter>
            {
                ["name"] = new()
                {
                    Type = "string",
                    Required = true
                }
            }
        };
        
        // Act
        var validation = template.Validate();
        
        // Assert
        validation.IsValid.ShouldBeFalse();
        validation.Errors.ShouldContain("Unclosed variable brackets");
    }
    
    [Fact]
    public async Task PromptTemplate_WithSystemPrompt_ShouldApply()
    {
        // Arrange
        var manager = new MockPromptManager();
        var template = new AevatarPromptTemplate
        {
            Name = "optimized",
            Content = "Process this: {{input}}",
            SystemPrompt = "Be concise and clear"
        };
        
        // Act
        var formatted = await manager.FormatTemplateAsync(
            template, 
            new Dictionary<string, object> { ["input"] = "long verbose text that should be concise" });
        
        // Assert
        formatted.ShouldContain("Process this:");
        // System prompt would be used in actual LLM call
    }
    
    [Fact]
    public async Task PromptTemplate_WithExamples_ShouldInclude()
    {
        // Arrange
        var template = new AevatarPromptTemplate
        {
            Name = "with-examples",
            Content = "Task: {{task}}\n\nExamples:\n{{examples}}",
            AevatarExamples = new List<AevatarExample>
            {
                new() { Input = "2+2", Output = "4" },
                new() { Input = "3*3", Output = "9" }
            }
        };
        
        // Act
        var formatted = template.FormatWithExamples("Calculate 5+5");
        
        // Assert
        formatted.ShouldContain("Task: Calculate 5+5");
        formatted.ShouldContain("2+2");
        formatted.ShouldContain("4");
    }
    
    [Fact]
    public void PromptTemplate_OutputFormat_ShouldBeRespected()
    {
        // Arrange
        var template = new AevatarPromptTemplate
        {
            Name = "json-output",
            Content = "Generate data for: {{entity}}",
            AevatarOutputFormat = new AevatarOutputFormat { Type = "json" }
        };
        
        // Act
        var prompt = template.BuildPrompt(new Dictionary<string, object> { ["entity"] = "user" });
        
        // Assert
        prompt.ShouldContain("Generate data for: user");
        prompt.ShouldContain("JSON"); // Should include format instruction
    }
}

/// <summary>
/// Mock implementation of prompt manager for testing
/// </summary>
public class MockPromptManager : IAevatarPromptManager
{
    private readonly Dictionary<string, string> _systemPrompts = new();
    private string _defaultPrompt = "Default system prompt";
    
    public bool SimulateDelay { get; set; }
    
    public void AddSystemPrompt(string key, string prompt)
    {
        _systemPrompts[key] = prompt;
    }
    
    public void SetDefaultPrompt(string prompt)
    {
        _defaultPrompt = prompt;
    }
    
    public async Task<string> GetSystemPromptAsync(string? key = null, CancellationToken cancellationToken = default)
    {
        if (SimulateDelay)
            await Task.Delay(100, cancellationToken);
        
        cancellationToken.ThrowIfCancellationRequested();
        
        if (string.IsNullOrEmpty(key))
            return _defaultPrompt;
        
        return _systemPrompts.TryGetValue(key, out var prompt) ? prompt : _defaultPrompt;
    }
    
    public async Task<string> FormatPromptAsync(
        string template, 
        Dictionary<string, object>? variables = null, 
        CancellationToken cancellationToken = default)
    {
        if (SimulateDelay)
            await Task.Delay(100, cancellationToken);
        
        cancellationToken.ThrowIfCancellationRequested();
        
        if (variables == null)
            return template;
        
        var result = template;
        foreach (var kvp in variables)
        {
            result = result.Replace($"{{{{{kvp.Key}}}}}", kvp.Value?.ToString() ?? "");
        }
        
        return await Task.FromResult(result);
    }
    
    public async Task<IList<AevatarChatMessage>> BuildChatPromptAsync(
        string systemPrompt, 
        IList<AevatarChatMessage>? history = null, 
        CancellationToken cancellationToken = default)
    {
        if (SimulateDelay)
            await Task.Delay(100, cancellationToken);
        
        cancellationToken.ThrowIfCancellationRequested();
        
        var messages = new List<AevatarChatMessage>
        {
            new() { Role = AevatarChatRole.System, Content = systemPrompt }
        };
        
        if (history != null)
        {
            messages.AddRange(history);
        }
        
        return messages;
    }
    
    public Task<string> FormatTemplateAsync(
        AevatarPromptTemplate template,
        Dictionary<string, object> variables,
        CancellationToken cancellationToken = default)
    {
        return FormatPromptAsync(template.Content, variables, cancellationToken);
    }
}

/// <summary>
/// Extensions for testing prompt templates
/// </summary>
public static class PromptTemplateExtensions
{
    public static AevatarPromptValidationResult Validate(this AevatarPromptTemplate template)
    {
        var result = new AevatarPromptValidationResult { IsValid = true };
        
        // Check for unclosed variables
        var openCount = template.Content.Count(c => c == '{');
        var closeCount = template.Content.Count(c => c == '}');
        
        if (openCount != closeCount)
        {
            result.IsValid = false;
            result.Errors.Add("Unclosed variable brackets");
        }
        
        return result;
    }
    
    public static string FormatWithExamples(this AevatarPromptTemplate template, string task)
    {
        var examples = string.Join("\n", 
            template.AevatarExamples?.Select(e => $"Input: {e.Input}\nOutput: {e.Output}") ?? Enumerable.Empty<string>());
        
        return template.Content
            .Replace("{{task}}", task)
            .Replace("{{examples}}", examples);
    }
    
    public static string BuildPrompt(this AevatarPromptTemplate template, Dictionary<string, object> variables)
    {
        var prompt = template.Content;
        
        foreach (var kvp in variables)
        {
            prompt = prompt.Replace($"{{{{{kvp.Key}}}}}", kvp.Value?.ToString() ?? "");
        }
        
        if (template.AevatarOutputFormat?.Type == "json")
        {
            prompt += "\n\nPlease respond in JSON format.";
        }
        
        return prompt;
    }
}
