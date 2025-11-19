using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core.Tests.Messages;

namespace Aevatar.Agents.AI.Core.Tests.TestAgents;

/// <summary>
/// Simplified test implementation of AIGAgentBase for unit testing
/// </summary>
public class TestAIGAgent(Guid? id = null) : AIGAgentBase<TestAIGAgentState, TestAIGAgentConfig>(id ?? Guid.NewGuid())
{
    /// <summary>
    /// Track initialization calls for testing
    /// </summary>
    public int InitializeCallCount { get; private set; }
    
    /// <summary>
    /// Track configuration calls for testing
    /// </summary>
    public int ConfigureAICallCount { get; private set; }
    
    /// <summary>
    /// Track custom configuration calls for testing
    /// </summary>
    public int ConfigureCustomCallCount { get; private set; }
    
    /// <summary>
    /// Custom system prompt for testing
    /// </summary>
    public string TestSystemPrompt { get; set; } = "Test AI Assistant";
    
    /// <summary>
    /// Override system prompt
    /// </summary>
    public override string SystemPrompt 
    { 
        get => TestSystemPrompt; 
        set => TestSystemPrompt = value; 
    }
    
    /// <summary>
    /// Override initialization to track calls
    /// </summary>
    public override async Task InitializeAsync(
        string providerName,
        Action<AevatarAIAgentConfiguration>? configureAI = null,
        CancellationToken cancellationToken = default)
    {
        InitializeCallCount++;
        await base.InitializeAsync(providerName, configureAI, cancellationToken);
    }
    
    /// <summary>
    /// Override initialization with custom config to track calls
    /// </summary>
    public override async Task InitializeAsync(
        LLMProviderConfig providerConfig,
        Action<AevatarAIAgentConfiguration>? configureAI = null,
        CancellationToken cancellationToken = default)
    {
        InitializeCallCount++;
        await base.InitializeAsync(providerConfig, configureAI, cancellationToken);
    }
    
    /// <summary>
    /// Override AI configuration to track calls
    /// </summary>
    protected override void ConfigureAI(AevatarAIAgentConfiguration config)
    {
        ConfigureAICallCount++;
        
        // Set test defaults
        config.Model = "test-model";
        config.Temperature = 0.5;
        config.MaxTokens = 1000;
        config.MaxHistory = 10;
    }
    
    /// <summary>
    /// Override custom configuration to track calls
    /// </summary>
    protected override void ConfigureCustom(TestAIGAgentConfig config)
    {
        ConfigureCustomCallCount++;
        
        // Set test defaults
        config.ConfigId = "test-config";
        config.Description = "Test configuration";
        config.MaxRetries = 3;
        config.TimeoutSeconds = 30.0;
        config.EnableLogging = true;
        config.BatchSize = 10;
        config.AllowedOperations.Add("read");
        config.AllowedOperations.Add("write");
        config.CustomSettings["test-key"] = "test-value";
    }
    
    /// <summary>
    /// Override description for testing
    /// </summary>
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"TestAIGAgent (Messages: {State.MessageCount})");
    }
    
    /// <summary>
    /// Test helper to check if initialized
    /// </summary>
    public bool IsInitialized => LLMProviderFactory != null;
    
    /// <summary>
    /// Test helper to reset counters
    /// </summary>
    public void ResetCounters()
    {
        InitializeCallCount = 0;
        ConfigureAICallCount = 0;
        ConfigureCustomCallCount = 0;
    }
    
    /// <summary>
    /// Test helper to get current AI configuration
    /// </summary>
    public AevatarAIAgentConfiguration GetAIConfiguration() => Configuration;
    
    /// <summary>
    /// Test helper to get current custom configuration
    /// </summary>
    public TestAIGAgentConfig GetCustomConfiguration() => Config;
    
    /// <summary>
    /// Test helper to get state (for testing only)
    /// </summary>
    public new TestAIGAgentState GetState() => State;
    
    /// <summary>
    /// Test helper to update state (simulates event handler context)
    /// </summary>
    public Task UpdateStateInContextAsync(Action<TestAIGAgentState> updateAction)
    {
        // Simulate being in an event handler context
        return Task.Run(() =>
        {
            updateAction(State);
        });
    }
}
