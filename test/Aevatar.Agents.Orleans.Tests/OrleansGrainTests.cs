using System;
using System.Threading.Tasks;
using Aevatar.Agents;
using Aevatar.Agents.Core;
using Aevatar.Agents.TestBase;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Orleans;
using Xunit;

namespace Aevatar.Agents.Orleans.Tests;

/// <summary>
/// Tests for Orleans Grain implementation
/// Focuses on Orleans-specific functionality
/// </summary>
public class OrleansGrainTests : AevatarAgentsTestBase
{
    private readonly IGrainFactory _grainFactory;
    
    public OrleansGrainTests(ClusterFixture fixture) : base(fixture)
    {
        _grainFactory = GrainFactory;
    }
    
    [Fact]
    public async Task Grain_Should_Activate_And_Return_Id()
    {
        // Arrange
        var grainId = Guid.NewGuid();
            // Use the standard grain implementation explicitly to avoid ambiguity
            var grain = _grainFactory.GetGrain<IStandardGAgentGrain>(grainId.ToString());
        
        // Act
        await grain.ActivateAsync("Aevatar.Agents.Orleans.Tests.TestGrainAgent", 
                                  "Aevatar.Agents.Orleans.Tests.TestGrainState");
        var retrievedId = await grain.GetIdAsync();
        
        // Assert
        Assert.Equal(grainId, retrievedId);
    }
    
    [Fact]
    public async Task Grain_Should_Manage_Parent_Child_Relationships()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        
        var parentGrain = _grainFactory.GetGrain<IStandardGAgentGrain>(parentId.ToString());
        var childGrain = _grainFactory.GetGrain<IStandardGAgentGrain>(childId.ToString());
        
        await parentGrain.ActivateAsync();
        await childGrain.ActivateAsync();
        
        // Act - Establish parent-child relationship
        await parentGrain.AddChildAsync(childId);
        await childGrain.SetParentAsync(parentId);
        
        // Assert
        var children = await parentGrain.GetChildrenAsync();
        var parent = await childGrain.GetParentAsync();
        
        Assert.Contains(childId, children);
        Assert.Equal(parentId, parent);
    }
    
    [Fact]
    public async Task Grain_Should_Remove_Child_Correctly()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        
        var parentGrain = _grainFactory.GetGrain<IStandardGAgentGrain>(parentId.ToString());
        var childGrain = _grainFactory.GetGrain<IStandardGAgentGrain>(childId.ToString());
        
        await parentGrain.ActivateAsync();
        await childGrain.ActivateAsync();
        
        await parentGrain.AddChildAsync(childId);
        await childGrain.SetParentAsync(parentId);
        
        // Act
        await parentGrain.RemoveChildAsync(childId);
        
        // Assert
        var children = await parentGrain.GetChildrenAsync();
        Assert.DoesNotContain(childId, children);
    }
    
    [Fact]
    public async Task Grain_Should_Clear_Parent_Correctly()
    {
        // Arrange
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        
        var parentGrain = _grainFactory.GetGrain<IStandardGAgentGrain>(parentId.ToString());
        var childGrain = _grainFactory.GetGrain<IStandardGAgentGrain>(childId.ToString());
        
        await parentGrain.ActivateAsync();
        await childGrain.ActivateAsync();
        
        await childGrain.SetParentAsync(parentId);
        
        // Act
        await childGrain.ClearParentAsync();
        
        // Assert
        var parent = await childGrain.GetParentAsync();
        Assert.Null(parent);
    }
    
    [Fact(Skip = "Grain event handling requires Agent setup - handled at Actor level in simplified architecture")]
    public async Task Grain_Should_Handle_Event_Bytes()
    {
        // This test is skipped because in the simplified architecture:
        // 1. Grains don't directly hold Agent instances
        // 2. Event handling is done at the Actor level (OrleansGAgentActor)
        // 3. Direct grain event handling would require Agent setup which is not part of the grain's responsibilities
        
        // Arrange
        var grainId = Guid.NewGuid();
        // Use the standard grain implementation explicitly to avoid ambiguity
        var grain = _grainFactory.GetGrain<IStandardGAgentGrain>(grainId.ToString());
        
        await grain.ActivateAsync();
        
        // Create an event envelope
        var testEvent = new StringValue { Value = "test message" };
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            PublisherId = Guid.NewGuid().ToString(),
            Direction = EventDirection.Down,
            CurrentHopCount = 0,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        
        // Set the payload using protobuf Any
        envelope.Payload = Google.Protobuf.WellKnownTypes.Any.Pack(testEvent);
        
        // Serialize envelope to bytes
        var envelopeBytes = envelope.ToByteArray();
        
        // Act - Send event bytes to grain
        // This would fail with "Agent not set" in the current architecture
        // await grain.HandleEventAsync(envelopeBytes);
        
        // Assert - No exception thrown
        // (Actual event handling verification would require access to internal state)
    }
    
    [Fact]
    public async Task Multiple_Grains_Should_Work_Independently()
    {
        // Arrange
        var grain1Id = Guid.NewGuid();
        var grain2Id = Guid.NewGuid();
        
        var grain1 = _grainFactory.GetGrain<IStandardGAgentGrain>(grain1Id.ToString());
        var grain2 = _grainFactory.GetGrain<IStandardGAgentGrain>(grain2Id.ToString());
        
        await grain1.ActivateAsync();
        await grain2.ActivateAsync();
        
        // Act
        await grain1.AddChildAsync(Guid.NewGuid());
        await grain2.AddChildAsync(Guid.NewGuid());
        await grain2.AddChildAsync(Guid.NewGuid());
        
        // Assert - Each grain maintains its own state
        var children1 = await grain1.GetChildrenAsync();
        var children2 = await grain2.GetChildrenAsync();
        
        Assert.Single(children1);
        Assert.Equal(2, children2.Count);
    }
    
    [Fact]
    public async Task Grain_Should_Handle_Deactivation()
    {
        // Arrange
        var grainId = Guid.NewGuid();
            // Use the standard grain implementation explicitly to avoid ambiguity
            var grain = _grainFactory.GetGrain<IStandardGAgentGrain>(grainId.ToString());
        
        await grain.ActivateAsync();
        
        // Act
        await grain.DeactivateAsync();
        
        // Assert - Should not throw
        // After deactivation, grain can be reactivated
        await grain.ActivateAsync();
        var id = await grain.GetIdAsync();
        Assert.Equal(grainId, id);
    }
    
    [Fact]
    public async Task Grain_Should_Handle_Complex_Hierarchy()
    {
        // Arrange - Create a three-level hierarchy
        var rootId = Guid.NewGuid();
        var middleId = Guid.NewGuid();
        var leafId = Guid.NewGuid();
        
        var rootGrain = _grainFactory.GetGrain<IStandardGAgentGrain>(rootId.ToString());
        var middleGrain = _grainFactory.GetGrain<IStandardGAgentGrain>(middleId.ToString());
        var leafGrain = _grainFactory.GetGrain<IStandardGAgentGrain>(leafId.ToString());
        
        await rootGrain.ActivateAsync();
        await middleGrain.ActivateAsync();
        await leafGrain.ActivateAsync();
        
        // Act - Build hierarchy
        await rootGrain.AddChildAsync(middleId);
        await middleGrain.SetParentAsync(rootId);
        await middleGrain.AddChildAsync(leafId);
        await leafGrain.SetParentAsync(middleId);
        
        // Assert
        var rootChildren = await rootGrain.GetChildrenAsync();
        var middleParent = await middleGrain.GetParentAsync();
        var middleChildren = await middleGrain.GetChildrenAsync();
        var leafParent = await leafGrain.GetParentAsync();
        
        Assert.Contains(middleId, rootChildren);
        Assert.Equal(rootId, middleParent);
        Assert.Contains(leafId, middleChildren);
        Assert.Equal(middleId, leafParent);
    }
}
