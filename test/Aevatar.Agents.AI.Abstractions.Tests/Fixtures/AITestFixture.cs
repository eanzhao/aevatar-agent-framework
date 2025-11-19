using System.IO;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Abstractions.Tests.LLMProvider;
using Aevatar.Agents.AI.Abstractions.Tests.Memory;
using Aevatar.Agents.AI.Abstractions.Tests.ToolManager;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Abstractions.Tests.Fixtures;

/// <summary>
/// Test fixture for AI abstraction tests with DI and configuration support
/// </summary>
public class AITestFixture : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    
    public IServiceProvider ServiceProvider => _serviceProvider;
    public IConfiguration Configuration { get; }
    public ILogger<AITestFixture> Logger { get; }

    public AITestFixture()
    {
        // Build configuration
        // Use the assembly location to find appsettings.test.json
        var assemblyLocation = Path.GetDirectoryName(typeof(AITestFixture).Assembly.Location);
        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(assemblyLocation ?? Directory.GetCurrentDirectory())
            .AddInMemoryCollection(GetDefaultConfiguration()) // Default values first
            .AddJsonFile("appsettings.test.json", optional: true, reloadOnChange: false); // JSON overrides defaults

        Configuration = configBuilder.Build();

        // Setup DI container
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // Get logger
        Logger = _serviceProvider.GetRequiredService<ILogger<AITestFixture>>();
        Logger.LogInformation("AI Test Fixture initialized");
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole()
                   .SetMinimumLevel(LogLevel.Debug);
        });

        // Add configuration
        services.AddSingleton(Configuration);

        // Configure LLM providers
        services.Configure<LLMProvidersConfig>(Configuration.GetSection("LLMProviders"));
        
        // Register test implementations
        services.AddSingleton<ILLMProviderFactory, MockLLMProviderFactory>();
        
        // Add Options
        services.AddOptions();

        // Register memory and tool managers for testing
        services.AddSingleton<IAevatarAIMemory, MockMemory>();
        services.AddSingleton<IAevatarToolManager>(sp =>
        {
            var toolManager = new MockToolManager();
            // Register a default tool for testing
            toolManager.RegisterTool(new ToolDefinition
            {
                Name = "calculator",
                Description = "Perform calculations",
                Category = ToolCategory.Utility,
                IsEnabled = true,
                Parameters = new ToolParameters
                {
                    Items = new Dictionary<string, ToolParameter>
                    {
                        ["expression"] = new() { Type = "string", Description = "Math expression", Required = true }
                    },
                    Required = new List<string> { "expression" }
                }
            });
            return toolManager;
        });
        
        // Add any other test services
        ConfigureTestServices(services);
    }

    /// <summary>
    /// Override this method in derived fixtures to add specific test services
    /// </summary>
    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
        // Can be overridden in derived classes
    }

    private Dictionary<string, string?> GetDefaultConfiguration()
    {
        return new Dictionary<string, string?>
        {
            // Default LLM configuration
            ["LLMProviders:Default"] = "test-provider",
            ["LLMProviders:Providers:test-provider:Name"] = "test-provider",
            ["LLMProviders:Providers:test-provider:ProviderType"] = "test",
            ["LLMProviders:Providers:test-provider:Model"] = "test-model",
            ["LLMProviders:Providers:test-provider:ApiKey"] = "test-api-key",
            ["LLMProviders:Providers:test-provider:Temperature"] = "0.7",
            ["LLMProviders:Providers:test-provider:MaxTokens"] = "2048",
            
            ["LLMProviders:Providers:openai-gpt4:Name"] = "openai-gpt4",
            ["LLMProviders:Providers:openai-gpt4:ProviderType"] = "openai",
            ["LLMProviders:Providers:openai-gpt4:Model"] = "gpt-4",
            ["LLMProviders:Providers:openai-gpt4:ApiKey"] = "fake-openai-key",
            ["LLMProviders:Providers:openai-gpt4:Temperature"] = "0.5",
            ["LLMProviders:Providers:openai-gpt4:MaxTokens"] = "4096",
            
            ["LLMProviders:Providers:azure-gpt35:Name"] = "azure-gpt35",
            ["LLMProviders:Providers:azure-gpt35:ProviderType"] = "azure",
            ["LLMProviders:Providers:azure-gpt35:Model"] = "gpt-35-turbo",
            ["LLMProviders:Providers:azure-gpt35:DeploymentName"] = "gpt35-deployment",
            ["LLMProviders:Providers:azure-gpt35:ApiKey"] = "fake-azure-key",
            ["LLMProviders:Providers:azure-gpt35:Endpoint"] = "https://test.openai.azure.com/",
            ["LLMProviders:Providers:azure-gpt35:Temperature"] = "0.3",
            ["LLMProviders:Providers:azure-gpt35:MaxTokens"] = "2048",

            // Test AI agent configuration
            ["AIAgents:MaxChainOfAevatarThoughtSteps"] = "10",
            ["AIAgents:MaxReActIterations"] = "15",
            ["AIAgents:DefaultProcessingMode"] = "Auto",
            
            // Logging configuration
            ["Logging:LogLevel:Default"] = "Information",
            ["Logging:LogLevel:Aevatar"] = "Debug"
        };
    }

    /// <summary>
    /// Get a service from the DI container
    /// </summary>
    public T GetService<T>() where T : notnull
    {
        return _serviceProvider.GetRequiredService<T>();
    }

    /// <summary>
    /// Get an optional service from the DI container
    /// </summary>
    public T? GetOptionalService<T>() where T : class
    {
        return _serviceProvider.GetService<T>();
    }

    /// <summary>
    /// Create a new scope for testing
    /// </summary>
    public IServiceScope CreateScope()
    {
        return _serviceProvider.CreateScope();
    }

    public void Dispose()
    {
        Logger.LogInformation("AI Test Fixture disposing");
        _serviceProvider?.Dispose();
    }
}

/// <summary>
/// Extended test fixture for specific test scenarios
/// </summary>
public class ExtendedAITestFixture : AITestFixture
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        
        // Add additional test services
        services.AddSingleton<ICustomTestService, CustomTestService>();
    }
}

// Example custom service for testing
public interface ICustomTestService
{
    string GetTestData();
}

public class CustomTestService : ICustomTestService
{
    private readonly ILogger<CustomTestService> _logger;

    public CustomTestService(ILogger<CustomTestService> logger)
    {
        _logger = logger;
    }

    public string GetTestData()
    {
        _logger.LogDebug("Getting test data");
        return "Test data from custom service";
    }
}