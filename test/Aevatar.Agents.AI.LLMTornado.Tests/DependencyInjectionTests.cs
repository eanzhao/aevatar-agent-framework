using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.LLMTornado;
using Aevatar.Agents.AI.LLMTornadoExtension;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using LlmTornado.Code;

namespace Aevatar.Agents.AI.LLMTornado.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void AddAevatarLLMTornado_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddAevatarLLMTornado(config =>
        {
            config.ApiKey = "test-key";
            config.Provider = LLmProviders.OpenAi;
        });

        var provider = services.BuildServiceProvider();

        // Assert
        var llmProvider = provider.GetService<IAevatarLLMProvider>();
        llmProvider.ShouldNotBeNull();
        llmProvider.ShouldBeOfType<LLMTornadoProvider>();
    }
}
