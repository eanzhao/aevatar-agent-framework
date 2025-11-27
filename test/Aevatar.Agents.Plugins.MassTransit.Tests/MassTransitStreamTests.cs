using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Plugins.MassTransit;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Plugins.MassTransit.Tests;

public class MassTransitStreamTests
{
    [Fact]
    public async Task Should_Receive_Message_In_Same_Stream()
    {
        // Arrange
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<StreamMessageDispatcher>();
            })
            .AddSingleton<MassTransitMessageStreamProvider>()
            .Configure<MassTransitStreamOptions>(options =>
            {
                options.TransportType = MassTransitTransportType.InMemory;
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var streamProvider = provider.GetRequiredService<MassTransitMessageStreamProvider>();
        var streamId = Guid.NewGuid();
        var stream = streamProvider.GetStream(streamId);

        var receivedTcs = new TaskCompletionSource<EventEnvelope>();

        // Act
        await stream.SubscribeAsync<EventEnvelope>(async env =>
        {
            receivedTcs.SetResult(env);
            await Task.CompletedTask;
        });

        var testEvent = new EventEnvelope 
        { 
            Id = "test-event-1",
            PublisherId = streamId.ToString(),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };
        
        await stream.ProduceAsync(testEvent);

        // Assert
        // Wait for message to be consumed and dispatched
        var received = await receivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        
        received.ShouldNotBeNull();
        received.Id.ShouldBe(testEvent.Id);
        
        // Verify MassTransit consumption
        (await harness.Consumed.Any<ByteArrayMessage>()).ShouldBeTrue();
    }
    
    [Fact]
    public async Task Should_Not_Receive_Message_From_Different_Stream()
    {
        // Arrange
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<StreamMessageDispatcher>();
            })
            .AddSingleton<MassTransitMessageStreamProvider>()
            .Configure<MassTransitStreamOptions>(options =>
            {
                options.TransportType = MassTransitTransportType.InMemory;
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var streamProvider = provider.GetRequiredService<MassTransitMessageStreamProvider>();
        
        var stream1 = streamProvider.GetStream(Guid.NewGuid());
        var stream2 = streamProvider.GetStream(Guid.NewGuid());

        var received1 = false;
        var received2 = false;

        await stream1.SubscribeAsync<EventEnvelope>(async _ => 
        {
            received1 = true;
            await Task.CompletedTask;
        });

        await stream2.SubscribeAsync<EventEnvelope>(async _ => 
        {
            received2 = true;
            await Task.CompletedTask;
        });

        var testEvent = new EventEnvelope 
        { 
            Id = "test-event",
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        // Act - Produce to Stream 1
        await stream1.ProduceAsync(testEvent);

        // Wait a bit to ensure processing
        // In real tests, we should wait for condition, but for simplicity:
        await Task.Delay(1000);

        // Assert
        received1.ShouldBeTrue();
        received2.ShouldBeFalse();
        
        // Verify MassTransit consumption
        (await harness.Consumed.Any<ByteArrayMessage>()).ShouldBeTrue();
    }

    [Fact]
    public async Task Should_Respect_Filter()
    {
        // Arrange
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<StreamMessageDispatcher>();
            })
            .AddSingleton<MassTransitMessageStreamProvider>()
            .Configure<MassTransitStreamOptions>(options =>
            {
                options.TransportType = MassTransitTransportType.InMemory;
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var streamProvider = provider.GetRequiredService<MassTransitMessageStreamProvider>();
        var stream = streamProvider.GetStream(Guid.NewGuid());

        var receivedCount = 0;

        await stream.SubscribeAsync<EventEnvelope>(
            async _ => 
            {
                Interlocked.Increment(ref receivedCount);
                await Task.CompletedTask;
            },
            filter: env => env.Id.StartsWith("valid") // Filter logic
        );

        // Act
        await stream.ProduceAsync(new EventEnvelope { Id = "valid-1", Timestamp = Timestamp.FromDateTime(DateTime.UtcNow) });
        await stream.ProduceAsync(new EventEnvelope { Id = "invalid-1", Timestamp = Timestamp.FromDateTime(DateTime.UtcNow) });

        // Wait
        await Task.Delay(1000);

        // Assert
        receivedCount.ShouldBe(1);
    }
}

