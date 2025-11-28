using Aevatar.Agents.Abstractions;
using FluentAssertions;
using Xunit;

namespace Aevatar.Agents.Core.Tests;

// ============================================================
//  ResourceContext Tests - Type-Safe Resource Management API
// ============================================================

public class ResourceContextTests
{
    // --------------------------------------------------------
    //  Initialization Tests
    // --------------------------------------------------------
    
    [Fact(DisplayName = "ResourceContext should initialize with empty collections")]
    public void ResourceContext_ShouldInitializeWithEmptyCollections()
    {
        // Arrange & Act
        var context = new ResourceContext();
        
        // Assert
        context.Count.Should().Be(0);
        context.Keys.Should().BeEmpty();
        context.Metadata.Should().NotBeNull();
        context.Metadata.Should().BeEmpty();
    }
    
    // --------------------------------------------------------
    //  Set<T> Tests
    // --------------------------------------------------------
    
    [Fact(DisplayName = "Set should add resource and metadata correctly")]
    public void Set_ShouldAddResourceAndMetadataCorrectly()
    {
        // Arrange
        var context = new ResourceContext();
        var resource = new TestResource { Id = 1, Name = "TestResource" };
        const string key = "test-resource";
        const string description = "A test resource";
        
        // Act
        context.Set(key, resource, description);
        
        // Assert
        context.Contains(key).Should().BeTrue();
        context.Get<TestResource>(key).Should().Be(resource);
        
        context.Metadata.Should().ContainKey(key);
        var metadata = context.Metadata[key];
        metadata.Key.Should().Be(key);
        metadata.Type.Should().Be("TestResource");
        metadata.Description.Should().Be(description);
        metadata.AddedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }
    
    [Fact(DisplayName = "Set should use empty description when not provided")]
    public void Set_ShouldUseEmptyDescriptionWhenNotProvided()
    {
        // Arrange
        var context = new ResourceContext();
        var resource = new TestResource { Id = 1, Name = "TestResource" };
        const string key = "test-resource";
        
        // Act
        context.Set(key, resource);
        
        // Assert
        context.Metadata[key].Description.Should().BeEmpty();
    }
    
    [Fact(DisplayName = "Set should overwrite existing resource with same key")]
    public void Set_ShouldOverwriteExistingResource()
    {
        // Arrange
        var context = new ResourceContext();
        var resource1 = new TestResource { Id = 1, Name = "Resource1" };
        var resource2 = new TestResource { Id = 2, Name = "Resource2" };
        const string key = "test-resource";
        
        // Act
        context.Set(key, resource1, "First resource");
        context.Set(key, resource2, "Second resource");
        
        // Assert
        context.Get<TestResource>(key).Should().Be(resource2);
        context.Metadata[key].Description.Should().Be("Second resource");
        context.Count.Should().Be(1);
    }
    
    [Fact(DisplayName = "Set should throw when value is null")]
    public void Set_ShouldThrowWhenValueIsNull()
    {
        // Arrange
        var context = new ResourceContext();
        
        // Act
        var act = () => context.Set<TestResource>("key", null!);
        
        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
    
    // --------------------------------------------------------
    //  Get<T> Tests
    // --------------------------------------------------------
    
    [Fact(DisplayName = "Get should return correct resource when it exists")]
    public void Get_ShouldReturnCorrectResourceWhenExists()
    {
        // Arrange
        var context = new ResourceContext();
        var resource = new TestResource { Id = 1, Name = "TestResource" };
        const string key = "test-resource";
        context.Set(key, resource);
        
        // Act
        var retrievedResource = context.Get<TestResource>(key);
        
        // Assert
        retrievedResource.Should().NotBeNull();
        retrievedResource.Should().Be(resource);
    }
    
    [Fact(DisplayName = "Get should return null when resource does not exist")]
    public void Get_ShouldReturnNullWhenResourceDoesNotExist()
    {
        // Arrange
        var context = new ResourceContext();
        
        // Act
        var retrievedResource = context.Get<TestResource>("non-existent");
        
        // Assert
        retrievedResource.Should().BeNull();
    }
    
    [Fact(DisplayName = "Get should return null when type does not match")]
    public void Get_ShouldReturnNullWhenTypeDoesNotMatch()
    {
        // Arrange
        var context = new ResourceContext();
        var resource = new TestResource { Id = 1, Name = "TestResource" };
        const string key = "test-resource";
        context.Set(key, resource);
        
        // Act
        var retrievedResource = context.Get<AnotherTestResource>(key);
        
        // Assert
        retrievedResource.Should().BeNull();
    }
    
    // --------------------------------------------------------
    //  GetRequired<T> Tests
    // --------------------------------------------------------
    
    [Fact(DisplayName = "GetRequired should return resource when it exists")]
    public void GetRequired_ShouldReturnResourceWhenExists()
    {
        // Arrange
        var context = new ResourceContext();
        var resource = new TestResource { Id = 42, Name = "Required" };
        const string key = "required-resource";
        context.Set(key, resource);
        
        // Act
        var result = context.GetRequired<TestResource>(key);
        
        // Assert
        result.Should().BeSameAs(resource);
    }
    
    [Fact(DisplayName = "GetRequired should throw KeyNotFoundException when resource does not exist")]
    public void GetRequired_ShouldThrowKeyNotFoundWhenNotExists()
    {
        // Arrange
        var context = new ResourceContext();
        
        // Act
        var act = () => context.GetRequired<TestResource>("non-existent");
        
        // Assert
        act.Should().Throw<KeyNotFoundException>()
            .WithMessage("*'non-existent'*not found*");
    }
    
    [Fact(DisplayName = "GetRequired should throw InvalidCastException when type does not match")]
    public void GetRequired_ShouldThrowInvalidCastWhenTypeMismatch()
    {
        // Arrange
        var context = new ResourceContext();
        var resource = new TestResource { Id = 1, Name = "Test" };
        const string key = "test-resource";
        context.Set(key, resource);
        
        // Act
        var act = () => context.GetRequired<AnotherTestResource>(key);
        
        // Assert
        act.Should().Throw<InvalidCastException>()
            .WithMessage("*'test-resource'*not of type*AnotherTestResource*");
    }
    
    // --------------------------------------------------------
    //  TryGet<T> Tests
    // --------------------------------------------------------
    
    [Fact(DisplayName = "TryGet should return true and output value when resource exists")]
    public void TryGet_ShouldReturnTrueAndOutputValueWhenExists()
    {
        // Arrange
        var context = new ResourceContext();
        var resource = new TestResource { Id = 1, Name = "Test" };
        const string key = "test-resource";
        context.Set(key, resource);
        
        // Act
        var result = context.TryGet<TestResource>(key, out var value);
        
        // Assert
        result.Should().BeTrue();
        value.Should().BeSameAs(resource);
    }
    
    [Fact(DisplayName = "TryGet should return false and null when resource does not exist")]
    public void TryGet_ShouldReturnFalseWhenNotExists()
    {
        // Arrange
        var context = new ResourceContext();
        
        // Act
        var result = context.TryGet<TestResource>("non-existent", out var value);
        
        // Assert
        result.Should().BeFalse();
        value.Should().BeNull();
    }
    
    [Fact(DisplayName = "TryGet should return false and null when type does not match")]
    public void TryGet_ShouldReturnFalseWhenTypeMismatch()
    {
        // Arrange
        var context = new ResourceContext();
        var resource = new TestResource { Id = 1, Name = "Test" };
        context.Set("test-resource", resource);
        
        // Act
        var result = context.TryGet<AnotherTestResource>("test-resource", out var value);
        
        // Assert
        result.Should().BeFalse();
        value.Should().BeNull();
    }
    
    // --------------------------------------------------------
    //  Contains Tests
    // --------------------------------------------------------
    
    [Fact(DisplayName = "Contains should return true when resource exists")]
    public void Contains_ShouldReturnTrueWhenExists()
    {
        // Arrange
        var context = new ResourceContext();
        context.Set("key", new TestResource { Id = 1, Name = "Test" });
        
        // Act & Assert
        context.Contains("key").Should().BeTrue();
    }
    
    [Fact(DisplayName = "Contains should return false when resource does not exist")]
    public void Contains_ShouldReturnFalseWhenNotExists()
    {
        // Arrange
        var context = new ResourceContext();
        
        // Act & Assert
        context.Contains("non-existent").Should().BeFalse();
    }
    
    // --------------------------------------------------------
    //  Remove Tests
    // --------------------------------------------------------
    
    [Fact(DisplayName = "Remove should remove resource and metadata")]
    public void Remove_ShouldRemoveResourceAndMetadata()
    {
        // Arrange
        var context = new ResourceContext();
        var resource = new TestResource { Id = 1, Name = "TestResource" };
        const string key = "test-resource";
        context.Set(key, resource);
        
        // Act
        var result = context.Remove(key);
        
        // Assert
        result.Should().BeTrue();
        context.Contains(key).Should().BeFalse();
        context.Metadata.Should().NotContainKey(key);
        context.Count.Should().Be(0);
    }
    
    [Fact(DisplayName = "Remove should return false when resource does not exist")]
    public void Remove_ShouldReturnFalseWhenResourceDoesNotExist()
    {
        // Arrange
        var context = new ResourceContext();
        
        // Act
        var result = context.Remove("non-existent");
        
        // Assert
        result.Should().BeFalse();
    }
    
    // --------------------------------------------------------
    //  Keys & Count Tests
    // --------------------------------------------------------
    
    [Fact(DisplayName = "Keys should return all resource keys")]
    public void Keys_ShouldReturnAllResourceKeys()
    {
        // Arrange
        var context = new ResourceContext();
        context.Set("key1", new TestResource { Id = 1, Name = "R1" });
        context.Set("key2", new AnotherTestResource { Value = "V2" });
        context.Set("key3", new TestResource { Id = 3, Name = "R3" });
        
        // Act
        var keys = context.Keys.ToList();
        
        // Assert
        keys.Should().HaveCount(3);
        keys.Should().Contain(new[] { "key1", "key2", "key3" });
    }
    
    [Fact(DisplayName = "Count should return correct number of resources")]
    public void Count_ShouldReturnCorrectNumber()
    {
        // Arrange
        var context = new ResourceContext();
        
        // Act & Assert
        context.Count.Should().Be(0);
        
        context.Set("key1", new TestResource { Id = 1, Name = "R1" });
        context.Count.Should().Be(1);
        
        context.Set("key2", new AnotherTestResource { Value = "V2" });
        context.Count.Should().Be(2);
        
        context.Remove("key1");
        context.Count.Should().Be(1);
    }
    
    // --------------------------------------------------------
    //  Clear Tests
    // --------------------------------------------------------
    
    [Fact(DisplayName = "Clear should remove all resources and metadata")]
    public void Clear_ShouldRemoveAllResourcesAndMetadata()
    {
        // Arrange
        var context = new ResourceContext();
        context.Set("key1", new TestResource { Id = 1, Name = "R1" });
        context.Set("key2", new AnotherTestResource { Value = "V2" });
        context.Set("key3", new TestResource { Id = 3, Name = "R3" });
        
        // Act
        context.Clear();
        
        // Assert
        context.Count.Should().Be(0);
        context.Keys.Should().BeEmpty();
        context.Metadata.Should().BeEmpty();
    }
    
    // --------------------------------------------------------
    //  Integration / Complex Scenarios
    // --------------------------------------------------------
    
    [Fact(DisplayName = "ResourceContext should handle multiple resources correctly")]
    public void ResourceContext_ShouldHandleMultipleResourcesCorrectly()
    {
        // Arrange
        var context = new ResourceContext();
        var resource1 = new TestResource { Id = 1, Name = "Resource1" };
        var resource2 = new AnotherTestResource { Value = "Value2" };
        var resource3 = new TestResource { Id = 3, Name = "Resource3" };
        
        // Act
        context.Set("resource1", resource1, "First resource");
        context.Set("resource2", resource2, "Second resource");
        context.Set("resource3", resource3, "Third resource");
        
        // Assert
        context.Count.Should().Be(3);
        context.Metadata.Should().HaveCount(3);
        
        context.Get<TestResource>("resource1").Should().Be(resource1);
        context.Get<AnotherTestResource>("resource2").Should().Be(resource2);
        context.Get<TestResource>("resource3").Should().Be(resource3);
    }
    
    [Fact(DisplayName = "ResourceContext should support polymorphic retrieval")]
    public void ResourceContext_ShouldSupportPolymorphicRetrieval()
    {
        // Arrange
        var context = new ResourceContext();
        var derived = new DerivedResource { Id = 1, Name = "Derived", Extra = "Extra" };
        
        // Act
        context.Set("resource", derived);
        
        // Assert - can retrieve as base type
        context.Get<TestResource>("resource").Should().NotBeNull();
        context.Get<TestResource>("resource")!.Name.Should().Be("Derived");
        
        // Assert - can retrieve as derived type
        context.Get<DerivedResource>("resource").Should().NotBeNull();
        context.Get<DerivedResource>("resource")!.Extra.Should().Be("Extra");
    }
    
    [Fact(DisplayName = "ResourceContext workflow - add, update, verify, remove")]
    public void ResourceContext_WorkflowTest()
    {
        // Arrange
        var context = new ResourceContext();
        var initial = new TestResource { Id = 1, Name = "Initial" };
        var updated = new TestResource { Id = 1, Name = "Updated" };
        const string key = "workflow-resource";
        
        // Act & Assert - Add
        context.Set(key, initial, "Initial version");
        context.Get<TestResource>(key)!.Name.Should().Be("Initial");
        context.Metadata[key].Description.Should().Be("Initial version");
        
        // Act & Assert - Update
        context.Set(key, updated, "Updated version");
        context.Get<TestResource>(key)!.Name.Should().Be("Updated");
        context.Metadata[key].Description.Should().Be("Updated version");
        context.Count.Should().Be(1); // Still only one resource
        
        // Act & Assert - TryGet pattern
        if (context.TryGet<TestResource>(key, out var resource))
        {
            resource!.Name.Should().Be("Updated");
        }
        else
        {
            throw new Exception("Resource should exist");
        }
        
        // Act & Assert - Remove
        context.Remove(key);
        context.Contains(key).Should().BeFalse();
        context.TryGet<TestResource>(key, out _).Should().BeFalse();
    }
    
    // --------------------------------------------------------
    //  Helper Classes
    // --------------------------------------------------------
    
    private class TestResource
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
    }
    
    private class AnotherTestResource
    {
        public string Value { get; init; } = string.Empty;
    }
    
    private class DerivedResource : TestResource
    {
        public string Extra { get; init; } = string.Empty;
    }
}

// ============================================================
//  ResourceMetadata Tests
// ============================================================

public class ResourceMetadataTests
{
    [Fact(DisplayName = "ResourceMetadata should initialize with default values")]
    public void ResourceMetadata_ShouldInitializeWithDefaultValues()
    {
        // Arrange & Act
        var metadata = new ResourceMetadata();
        
        // Assert
        metadata.Key.Should().BeEmpty();
        metadata.Type.Should().BeEmpty();
        metadata.Description.Should().BeEmpty();
        metadata.AddedAt.Should().Be(default);
    }
    
    [Fact(DisplayName = "ResourceMetadata properties should be settable")]
    public void ResourceMetadata_PropertiesShouldBeSettable()
    {
        // Arrange
        var metadata = new ResourceMetadata();
        const string key = "test-key";
        const string type = "TestType";
        const string description = "Test description";
        var addedAt = DateTime.UtcNow;
        
        // Act
        metadata.Key = key;
        metadata.Type = type;
        metadata.Description = description;
        metadata.AddedAt = addedAt;
        
        // Assert
        metadata.Key.Should().Be(key);
        metadata.Type.Should().Be(type);
        metadata.Description.Should().Be(description);
        metadata.AddedAt.Should().Be(addedAt);
    }
}
