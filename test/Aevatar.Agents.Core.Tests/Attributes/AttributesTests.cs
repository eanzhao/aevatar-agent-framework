using System.Reflection;
using Aevatar.Agents.Abstractions;
using FluentAssertions;
using Xunit;

namespace Aevatar.Agents.Core.Tests.Attributes;

public class EventHandlerAttributeTests
{
    [Fact(DisplayName = "EventHandlerAttribute should have correct default values")]
    public void EventHandlerAttribute_ShouldHaveCorrectDefaultValues()
    {
        // Arrange & Act
        var attribute = new EventHandlerAttribute();
        
        // Assert
        attribute.Priority.Should().Be(0);
        attribute.AllowSelfHandling.Should().BeFalse();
    }
    
    [Fact(DisplayName = "EventHandlerAttribute should allow setting Priority")]
    public void EventHandlerAttribute_ShouldAllowSettingPriority()
    {
        // Arrange & Act
        var attribute = new EventHandlerAttribute { Priority = 10 };
        
        // Assert
        attribute.Priority.Should().Be(10);
    }
    
    [Fact(DisplayName = "EventHandlerAttribute should allow setting AllowSelfHandling")]
    public void EventHandlerAttribute_ShouldAllowSettingAllowSelfHandling()
    {
        // Arrange & Act
        var attribute = new EventHandlerAttribute { AllowSelfHandling = true };
        
        // Assert
        attribute.AllowSelfHandling.Should().BeTrue();
    }
    
    [Fact(DisplayName = "EventHandlerAttribute should only be applicable to methods")]
    public void EventHandlerAttribute_ShouldOnlyBeApplicableToMethods()
    {
        // Arrange
        var attributeType = typeof(EventHandlerAttribute);
        
        // Act
        var attributeUsage = attributeType.GetCustomAttribute<AttributeUsageAttribute>();
        
        // Assert
        attributeUsage.Should().NotBeNull();
        attributeUsage!.ValidOn.Should().Be(AttributeTargets.Method);
        attributeUsage.AllowMultiple.Should().BeFalse();
    }
    
    [Fact(DisplayName = "EventHandlerAttribute should be retrievable via reflection")]
    public void EventHandlerAttribute_ShouldBeRetrievableViaReflection()
    {
        // Arrange
        var testClass = new TestClassWithEventHandlers();
        var methodInfo = testClass.GetType().GetMethod(nameof(TestClassWithEventHandlers.HandleWithPriority));
        
        // Act
        var attribute = methodInfo?.GetCustomAttribute<EventHandlerAttribute>();
        
        // Assert
        attribute.Should().NotBeNull();
        attribute!.Priority.Should().Be(5);
        attribute.AllowSelfHandling.Should().BeTrue();
    }
}

public class AllEventHandlerAttributeTests
{
    [Fact(DisplayName = "AllEventHandlerAttribute should have correct default values")]
    public void AllEventHandlerAttribute_ShouldHaveCorrectDefaultValues()
    {
        // Arrange & Act
        var attribute = new AllEventHandlerAttribute();
        
        // Assert
        attribute.Priority.Should().Be(int.MaxValue);
        attribute.AllowSelfHandling.Should().BeFalse();
    }
    
    [Fact(DisplayName = "AllEventHandlerAttribute should allow setting Priority")]
    public void AllEventHandlerAttribute_ShouldAllowSettingPriority()
    {
        // Arrange & Act
        var attribute = new AllEventHandlerAttribute { Priority = 100 };
        
        // Assert
        attribute.Priority.Should().Be(100);
    }
    
    [Fact(DisplayName = "AllEventHandlerAttribute should allow setting AllowSelfHandling")]
    public void AllEventHandlerAttribute_ShouldAllowSettingAllowSelfHandling()
    {
        // Arrange & Act
        var attribute = new AllEventHandlerAttribute { AllowSelfHandling = true };
        
        // Assert
        attribute.AllowSelfHandling.Should().BeTrue();
    }
    
    [Fact(DisplayName = "AllEventHandlerAttribute should only be applicable to methods")]
    public void AllEventHandlerAttribute_ShouldOnlyBeApplicableToMethods()
    {
        // Arrange
        var attributeType = typeof(AllEventHandlerAttribute);
        
        // Act
        var attributeUsage = attributeType.GetCustomAttribute<AttributeUsageAttribute>();
        
        // Assert
        attributeUsage.Should().NotBeNull();
        attributeUsage!.ValidOn.Should().Be(AttributeTargets.Method);
        attributeUsage.AllowMultiple.Should().BeFalse();
    }
    
    [Fact(DisplayName = "AllEventHandlerAttribute should be retrievable via reflection")]
    public void AllEventHandlerAttribute_ShouldBeRetrievableViaReflection()
    {
        // Arrange
        var testClass = new TestClassWithEventHandlers();
        var methodInfo = testClass.GetType().GetMethod(nameof(TestClassWithEventHandlers.HandleAllEvents));
        
        // Act
        var attribute = methodInfo?.GetCustomAttribute<AllEventHandlerAttribute>();
        
        // Assert
        attribute.Should().NotBeNull();
        attribute!.Priority.Should().Be(1);
        attribute.AllowSelfHandling.Should().BeFalse();
    }
}

public class ConfigurationAttributeTests
{
    [Fact(DisplayName = "ConfigurationAttribute should be instantiable")]
    public void ConfigurationAttribute_ShouldBeInstantiable()
    {
        // Arrange & Act
        var attribute = new ConfigurationAttribute();
        
        // Assert
        attribute.Should().NotBeNull();
    }
    
    [Fact(DisplayName = "ConfigurationAttribute should only be applicable to methods")]
    public void ConfigurationAttribute_ShouldOnlyBeApplicableToMethods()
    {
        // Arrange
        var attributeType = typeof(ConfigurationAttribute);
        
        // Act
        var attributeUsage = attributeType.GetCustomAttribute<AttributeUsageAttribute>();
        
        // Assert
        attributeUsage.Should().NotBeNull();
        attributeUsage!.ValidOn.Should().Be(AttributeTargets.Method);
        attributeUsage.AllowMultiple.Should().BeFalse();
    }
    
    [Fact(DisplayName = "ConfigurationAttribute should be retrievable via reflection")]
    public void ConfigurationAttribute_ShouldBeRetrievableViaReflection()
    {
        // Arrange
        var testClass = new TestClassWithConfiguration();
        var methodInfo = testClass.GetType().GetMethod(nameof(TestClassWithConfiguration.HandleConfig));
        
        // Act
        var attribute = methodInfo?.GetCustomAttribute<ConfigurationAttribute>();
        
        // Assert
        attribute.Should().NotBeNull();
    }
}

public class AttributeCombinationTests
{
    [Fact(DisplayName = "Multiple attribute types should be distinguishable on same class")]
    public void MultipleAttributeTypes_ShouldBeDistinguishableOnSameClass()
    {
        // Arrange
        var testClass = new TestClassWithAllAttributes();
        var type = testClass.GetType();
        
        // Act
        var eventHandlerMethods = type.GetMethods()
            .Where(m => m.GetCustomAttribute<EventHandlerAttribute>() != null)
            .ToList();
        
        var allEventHandlerMethods = type.GetMethods()
            .Where(m => m.GetCustomAttribute<AllEventHandlerAttribute>() != null)
            .ToList();
        
        var configurationMethods = type.GetMethods()
            .Where(m => m.GetCustomAttribute<ConfigurationAttribute>() != null)
            .ToList();
        
        // Assert
        eventHandlerMethods.Should().HaveCount(1);
        allEventHandlerMethods.Should().HaveCount(1);
        configurationMethods.Should().HaveCount(1);
        
        // Verify they are different methods
        var allMethods = eventHandlerMethods
            .Concat(allEventHandlerMethods)
            .Concat(configurationMethods)
            .Select(m => m.Name)
            .Distinct()
            .ToList();
            
        allMethods.Should().HaveCount(3);
    }
    
    [Fact(DisplayName = "Attributes should maintain their property values when applied")]
    public void Attributes_ShouldMaintainPropertyValuesWhenApplied()
    {
        // Arrange
        var type = typeof(TestClassWithComplexAttributes);
        
        // Act & Assert - Method with custom priority
        var highPriorityMethod = type.GetMethod(nameof(TestClassWithComplexAttributes.HighPriorityHandler));
        var highPriorityAttr = highPriorityMethod?.GetCustomAttribute<EventHandlerAttribute>();
        highPriorityAttr.Should().NotBeNull();
        highPriorityAttr!.Priority.Should().Be(-10);
        highPriorityAttr.AllowSelfHandling.Should().BeFalse();
        
        // Act & Assert - Method with self handling allowed
        var selfHandlingMethod = type.GetMethod(nameof(TestClassWithComplexAttributes.SelfHandlingHandler));
        var selfHandlingAttr = selfHandlingMethod?.GetCustomAttribute<EventHandlerAttribute>();
        selfHandlingAttr.Should().NotBeNull();
        selfHandlingAttr!.Priority.Should().Be(0);
        selfHandlingAttr.AllowSelfHandling.Should().BeTrue();
        
        // Act & Assert - All event handler with custom settings
        var customAllEventMethod = type.GetMethod(nameof(TestClassWithComplexAttributes.CustomAllEventHandler));
        var customAllEventAttr = customAllEventMethod?.GetCustomAttribute<AllEventHandlerAttribute>();
        customAllEventAttr.Should().NotBeNull();
        customAllEventAttr!.Priority.Should().Be(50);
        customAllEventAttr.AllowSelfHandling.Should().BeTrue();
    }
}

// Test helper classes
internal class TestClassWithEventHandlers
{
    [EventHandler(Priority = 5, AllowSelfHandling = true)]
    public Task HandleWithPriority(TestEvent evt) => Task.CompletedTask;
    
    [AllEventHandler(Priority = 1)]
    public Task HandleAllEvents(EventEnvelope envelope) => Task.CompletedTask;
}

internal class TestClassWithConfiguration
{
    [Configuration]
    public Task HandleConfig(TestConfigState config) => Task.CompletedTask;
}

internal class TestClassWithAllAttributes
{
    [EventHandler]
    public Task HandleEvent(TestEvent evt) => Task.CompletedTask;
    
    [AllEventHandler]
    public Task HandleAll(EventEnvelope envelope) => Task.CompletedTask;
    
    [Configuration]
    public Task HandleConfiguration(TestConfigState config) => Task.CompletedTask;
}

internal class TestClassWithComplexAttributes
{
    [EventHandler(Priority = -10)]
    public Task HighPriorityHandler(TestEvent evt) => Task.CompletedTask;
    
    [EventHandler(AllowSelfHandling = true)]
    public Task SelfHandlingHandler(TestAddItemEvent evt) => Task.CompletedTask;
    
    [AllEventHandler(Priority = 50, AllowSelfHandling = true)]
    public Task CustomAllEventHandler(EventEnvelope envelope) => Task.CompletedTask;
}
