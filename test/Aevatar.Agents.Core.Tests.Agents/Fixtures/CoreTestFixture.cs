using Microsoft.Extensions.DependencyInjection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Persistence;
using Aevatar.Agents.Core.Persistence;
using Aevatar.Agents.Core.Tests.EventPublisher;

namespace Aevatar.Agents.Core.Tests.Fixtures;

/// <summary>
/// Test fixture for Core tests with dependency injection
/// </summary>
public class CoreTestFixture : IDisposable
{
    public IServiceProvider ServiceProvider { get; }
    public TestEventPublisher EventPublisher { get; }

    public CoreTestFixture()
    {
        var services = new ServiceCollection();

        // Register state stores
        services.AddSingleton(typeof(IStateStore<>), typeof(InMemoryStateStore<>));
        services.AddSingleton(typeof(IConfigStore<>), typeof(InMemoryConfigStore<>));

        // Register TestEventPublisher as singleton so we can access it for assertions
        var eventPublisher = new TestEventPublisher();
        EventPublisher = eventPublisher;
        services.AddSingleton<IEventPublisher>(eventPublisher);

        // Build service provider
        ServiceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        // Clear event publisher state
        EventPublisher?.Clear();
        
        if (ServiceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}