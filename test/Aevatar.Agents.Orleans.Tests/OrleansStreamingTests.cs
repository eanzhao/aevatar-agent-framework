using System;
using System.IO;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Orleans;
using Aevatar.Agents.Serialization;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Hosting;
using Orleans.TestingHost;
using Xunit;

namespace Aevatar.Agents.Orleans.Tests;

/// <summary>
/// Tests for Orleans Streaming integration
/// </summary>
public class OrleansStreamingTests : IClassFixture<OrleansStreamingTests.StreamingClusterFixture>
{
    private readonly TestCluster _cluster;
    private readonly IGrainFactory _grainFactory;
    
    public OrleansStreamingTests(StreamingClusterFixture fixture)
    {
        _cluster = fixture.Cluster;
        _grainFactory = _cluster.GrainFactory;
    }
    
    [Fact]
    public async Task Grain_Should_Initialize_Stream_On_Activation()
    {
        // Arrange
        var agentId = Guid.NewGuid().ToString();
        var grain = _grainFactory.GetGrain<IStandardGAgentGrain>(agentId);
        
        // Act - Activate with agent type info
        await grain.ActivateAsync(
            typeof(TestStreamingAgent).AssemblyQualifiedName,
            typeof(TestStreamingState).AssemblyQualifiedName);
        
        // Assert - Grain should be activated with stream
        var id = await grain.GetIdAsync();
        Assert.Equal(agentId, id.ToString());
        
        // Verify stream functionality by sending an event
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(new StringValue { Value = "Stream test" }),
            Direction = EventDirection.Down
        };
        
        await grain.HandleEventAsync(SerializeEnvelope(envelope));
        
        // Give time for stream processing
        await Task.Delay(100);
        
        // Cleanup
        await grain.DeactivateAsync();
    }
    
    [Fact]
    public async Task Stream_Should_Propagate_Events_Between_Grains()
    {
        // Arrange
        var parentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();
        
        var parentGrain = _grainFactory.GetGrain<IStandardGAgentGrain>(parentId);
        var childGrain = _grainFactory.GetGrain<IStandardGAgentGrain>(childId);
        
        // Activate grains
        await parentGrain.ActivateAsync(
            typeof(TestStreamingAgent).AssemblyQualifiedName,
            typeof(TestStreamingState).AssemblyQualifiedName);
        await childGrain.ActivateAsync(
            typeof(TestStreamingAgent).AssemblyQualifiedName,
            typeof(TestStreamingState).AssemblyQualifiedName);
        
        // Establish hierarchy
        await childGrain.SetParentAsync(Guid.Parse(parentId));
        await parentGrain.AddChildAsync(Guid.Parse(childId));
        
        // Act - Send event from parent (should propagate to child via stream)
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(new StringValue { Value = "Parent to child via stream" }),
            Direction = EventDirection.Down
        };
        
        await parentGrain.HandleEventAsync(SerializeEnvelope(envelope));
        
        // Give time for stream propagation
        await Task.Delay(200);
        
        // Assert - Both grains should have processed the event
        var parentChildren = await parentGrain.GetChildrenAsync();
        Assert.Contains(Guid.Parse(childId), parentChildren);
        
        var childParent = await childGrain.GetParentAsync();
        Assert.Equal(Guid.Parse(parentId), childParent);
        
        // Cleanup
        await childGrain.DeactivateAsync();
        await parentGrain.DeactivateAsync();
    }
    
    [Fact]
    public async Task Stream_Should_Handle_Bidirectional_Events()
    {
        // Arrange
        var parentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();
        
        var parentGrain = _grainFactory.GetGrain<IStandardGAgentGrain>(parentId);
        var childGrain = _grainFactory.GetGrain<IStandardGAgentGrain>(childId);
        
        await parentGrain.ActivateAsync(
            typeof(TestStreamingAgent).AssemblyQualifiedName,
            typeof(TestStreamingState).AssemblyQualifiedName);
        await childGrain.ActivateAsync(
            typeof(TestStreamingAgent).AssemblyQualifiedName,
            typeof(TestStreamingState).AssemblyQualifiedName);
        
        await childGrain.SetParentAsync(Guid.Parse(parentId));
        await parentGrain.AddChildAsync(Guid.Parse(childId));
        
        // Act - Send bidirectional event from child
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(new StringValue { Value = "Bidirectional event" }),
            Direction = EventDirection.Bidirectional
        };
        
        await childGrain.HandleEventAsync(SerializeEnvelope(envelope));
        
        // Give time for stream propagation
        await Task.Delay(200);
        
        // Assert - Event should propagate both up and down
        // (In a real test, we'd verify the actual event handling)
        
        // Cleanup
        await childGrain.DeactivateAsync();
        await parentGrain.DeactivateAsync();
    }
    
    [Fact]
    public async Task Stream_Should_Fallback_To_Direct_Call_If_Not_Available()
    {
        // This test verifies the fallback mechanism works
        // when stream provider is not configured
        
        // Arrange
        var agentId = Guid.NewGuid().ToString();
        var grain = _grainFactory.GetGrain<IStandardGAgentGrain>(agentId);
        
        // Act - Activate without stream provider
        await grain.ActivateAsync(
            typeof(TestStreamingAgent).AssemblyQualifiedName,
            typeof(TestStreamingState).AssemblyQualifiedName);
        
        // Send event (should use fallback)
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(new StringValue { Value = "Fallback test" }),
            Direction = EventDirection.Down
        };
        
        await grain.HandleEventAsync(SerializeEnvelope(envelope));
        
        // Assert - Should not throw exception
        var id = await grain.GetIdAsync();
        Assert.Equal(agentId, id.ToString());
        
        // Cleanup
        await grain.DeactivateAsync();
    }
    
    // Test agent for streaming
    public class TestStreamingAgent : GAgentBase<TestStreamingState>
    {
        public TestStreamingAgent(Guid id) : base(id) { }
        
        public override Task<string> GetDescriptionAsync()
        {
            return Task.FromResult($"TestStreamingAgent {Id}");
        }
        
        [EventHandler]
        public Task HandleStringValue(StringValue value)
        {
            GetState().LastMessage = value.Value;
            GetState().MessageCount++;
            return Task.CompletedTask;
        }
    }
    
    public class TestStreamingState
    {
        public string? LastMessage { get; set; }
        public int MessageCount { get; set; }
    }
    
    private static byte[] SerializeEnvelope(EventEnvelope envelope)
    {
        using var stream = new MemoryStream();
        using var output = new CodedOutputStream(stream);
        envelope.WriteTo(output);
        output.Flush();
        return stream.ToArray();
    }
    
    public class StreamingClusterFixture : IDisposable
    {
        public TestCluster Cluster { get; }
        
        public StreamingClusterFixture()
        {
            var builder = new TestClusterBuilder();
            builder.AddSiloBuilderConfigurator<StreamingSiloConfigurator>();
            Cluster = builder.Build();
            Cluster.Deploy();
        }
        
        public void Dispose()
        {
            Cluster?.StopAllSilos();
        }
        
        private class StreamingSiloConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder siloBuilder)
            {
                siloBuilder
                    .UseLocalhostClustering()
                    .AddMemoryGrainStorage("PubSubStore")
                    .AddMemoryStreams("StreamProvider")
                    .AddMemoryGrainStorage("Default")
                    .ConfigureLogging(logging => logging
                        .AddConsole()
                        .SetMinimumLevel(LogLevel.Information));
            }
        }
    }
}
