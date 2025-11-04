using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Local.Tests;

public class LocalGAgentActorFactoryTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly LocalGAgentActorFactory _factory;

    public LocalGAgentActorFactoryTests()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        
        // 使用自动发现模式，无需手动注册
        services.AddGAgentActorFactoryProvider();
        
        _serviceProvider = services.BuildServiceProvider();

        _factory = new LocalGAgentActorFactory(
            _serviceProvider,
            _serviceProvider.GetRequiredService<ILogger<LocalGAgentActorFactory>>());
    }

    [Fact]
    public async Task CreateAgentAsync_ShouldCreateAgent()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var actor = await _factory.CreateGAgentActorAsync<TestAgent>(id);

        // Assert
        Assert.NotNull(actor);
        Assert.Equal(id, actor.Id);

        // Cleanup
        await actor.DeactivateAsync();
    }

    [Fact]
    public async Task CreateAgentAsync_WithSameId_ShouldThrow()
    {
        // Arrange
        var id = Guid.NewGuid();
        await _factory.CreateGAgentActorAsync<TestAgent>(id);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _factory.CreateGAgentActorAsync<TestAgent>(id));
    }
}