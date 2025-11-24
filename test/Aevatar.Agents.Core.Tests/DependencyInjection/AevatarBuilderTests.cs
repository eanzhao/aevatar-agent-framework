using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventRouting;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Abstractions.Persistence;
using Aevatar.Agents.Core.DependencyInjection;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Runtime.Local;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.Core.Tests.DependencyInjection;

public class AevatarBuilderTests
{
    [Fact]
    public void AddAevatarAgentSystem_RegistersCoreServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddAevatarAgentSystem();

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IGAgentManager>().Should().NotBeNull();
        provider.GetRequiredService<IGAgentActorFactoryProvider>().Should().NotBeNull();
    }

    [Fact]
    public void UseLocalRuntime_ConfiguresRuntimeDependencies()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddAevatarAgentSystem(builder => builder.UseLocalRuntime());

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IGAgentActorFactory>().Should().NotBeNull();
    }

    [Fact]
    public void UseLocalRuntime_Twice_Throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        Action act = () => services.AddAevatarAgentSystem(builder =>
        {
            builder.UseLocalRuntime();
            builder.UseLocalRuntime();
        });

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void UseInMemoryStateStore_RegistersOpenGenericStore()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddAevatarAgentSystem(builder => builder.UseInMemoryStateStore());

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IStateStore<TestAgentState>>()
            .Should().NotBeNull();
    }
}

