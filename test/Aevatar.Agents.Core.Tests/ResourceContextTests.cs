using Aevatar.Agents.Abstractions;
using FluentAssertions;
using Xunit;

namespace Aevatar.Agents.Core.Tests;

public class ResourceContextTests
{
    [Fact(DisplayName = "ResourceContext should initialize with empty collections")]
    public void ResourceContext_ShouldInitializeWithEmptyCollections()
    {
        // Arrange & Act
        var context = new ResourceContext();
        
        // Assert
        context.AvailableResources.Should().NotBeNull();
        context.AvailableResources.Should().BeEmpty();
        context.Metadata.Should().NotBeNull();
        context.Metadata.Should().BeEmpty();
    }
    
    [Fact(DisplayName = "AddResource should add resource and metadata correctly")]
    public void AddResource_ShouldAddResourceAndMetadataCorrectly()
    {
        // Arrange
        var context = new ResourceContext();
        var resource = new TestResource { Id = 1, Name = "TestResource" };
        var key = "test-resource";
        var description = "A test resource";
        
        // Act
        context.AddResource(key, resource, description);
        
        // Assert
        context.AvailableResources.Should().ContainKey(key);
        context.AvailableResources[key].Should().Be(resource);
        
        context.Metadata.Should().ContainKey(key);
        var metadata = context.Metadata[key];
        metadata.Key.Should().Be(key);
        metadata.Type.Should().Be("TestResource");
        metadata.Description.Should().Be(description);
        metadata.AddedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }
    
    [Fact(DisplayName = "AddResource should use empty description when not provided")]
    public void AddResource_ShouldUseEmptyDescriptionWhenNotProvided()
    {
        // Arrange
        var context = new ResourceContext();
        var resource = new TestResource { Id = 1, Name = "TestResource" };
        var key = "test-resource";
        
        // Act
        context.AddResource(key, resource);
        
        // Assert
        context.Metadata[key].Description.Should().BeEmpty();
    }
    
    [Fact(DisplayName = "AddResource should overwrite existing resource with same key")]
    public void AddResource_ShouldOverwriteExistingResource()
    {
        // Arrange
        var context = new ResourceContext();
        var resource1 = new TestResource { Id = 1, Name = "Resource1" };
        var resource2 = new TestResource { Id = 2, Name = "Resource2" };
        var key = "test-resource";
        
        // Act
        context.AddResource(key, resource1, "First resource");
        context.AddResource(key, resource2, "Second resource");
        
        // Assert
        context.AvailableResources[key].Should().Be(resource2);
        context.Metadata[key].Description.Should().Be("Second resource");
    }
    
    [Fact(DisplayName = "GetResource should return correct resource when it exists")]
    public void GetResource_ShouldReturnCorrectResourceWhenExists()
    {
        // Arrange
        var context = new ResourceContext();
        var resource = new TestResource { Id = 1, Name = "TestResource" };
        var key = "test-resource";
        context.AddResource(key, resource);
        
        // Act
        var retrievedResource = context.GetResource<TestResource>(key);
        
        // Assert
        retrievedResource.Should().NotBeNull();
        retrievedResource.Should().Be(resource);
    }
    
    [Fact(DisplayName = "GetResource should return null when resource does not exist")]
    public void GetResource_ShouldReturnNullWhenResourceDoesNotExist()
    {
        // Arrange
        var context = new ResourceContext();
        
        // Act
        var retrievedResource = context.GetResource<TestResource>("non-existent");
        
        // Assert
        retrievedResource.Should().BeNull();
    }
    
    [Fact(DisplayName = "GetResource should return null when type does not match")]
    public void GetResource_ShouldReturnNullWhenTypeDoesNotMatch()
    {
        // Arrange
        var context = new ResourceContext();
        var resource = new TestResource { Id = 1, Name = "TestResource" };
        var key = "test-resource";
        context.AddResource(key, resource);
        
        // Act
        var retrievedResource = context.GetResource<AnotherTestResource>(key);
        
        // Assert
        retrievedResource.Should().BeNull();
    }
    
    [Fact(DisplayName = "RemoveResource should remove resource and metadata")]
    public void RemoveResource_ShouldRemoveResourceAndMetadata()
    {
        // Arrange
        var context = new ResourceContext();
        var resource = new TestResource { Id = 1, Name = "TestResource" };
        var key = "test-resource";
        context.AddResource(key, resource);
        
        // Act
        var result = context.RemoveResource(key);
        
        // Assert
        result.Should().BeTrue();
        context.AvailableResources.Should().NotContainKey(key);
        context.Metadata.Should().NotContainKey(key);
    }
    
    [Fact(DisplayName = "RemoveResource should return false when resource does not exist")]
    public void RemoveResource_ShouldReturnFalseWhenResourceDoesNotExist()
    {
        // Arrange
        var context = new ResourceContext();
        
        // Act
        var result = context.RemoveResource("non-existent");
        
        // Assert
        result.Should().BeFalse();
    }
    
    [Fact(DisplayName = "ResourceContext should handle multiple resources correctly")]
    public void ResourceContext_ShouldHandleMultipleResourcesCorrectly()
    {
        // Arrange
        var context = new ResourceContext();
        var resource1 = new TestResource { Id = 1, Name = "Resource1" };
        var resource2 = new AnotherTestResource { Value = "Value2" };
        var resource3 = new TestResource { Id = 3, Name = "Resource3" };
        
        // Act
        context.AddResource("resource1", resource1, "First resource");
        context.AddResource("resource2", resource2, "Second resource");
        context.AddResource("resource3", resource3, "Third resource");
        
        // Assert
        context.AvailableResources.Should().HaveCount(3);
        context.Metadata.Should().HaveCount(3);
        
        context.GetResource<TestResource>("resource1").Should().Be(resource1);
        context.GetResource<AnotherTestResource>("resource2").Should().Be(resource2);
        context.GetResource<TestResource>("resource3").Should().Be(resource3);
    }
    
    // Helper classes for testing
    private class TestResource
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
    
    private class AnotherTestResource
    {
        public string Value { get; set; } = string.Empty;
    }
}

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
        metadata.AddedAt.Should().Be(default(DateTime));
    }
    
    [Fact(DisplayName = "ResourceMetadata properties should be settable")]
    public void ResourceMetadata_PropertiesShouldBeSettable()
    {
        // Arrange
        var metadata = new ResourceMetadata();
        var key = "test-key";
        var type = "TestType";
        var description = "Test description";
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
