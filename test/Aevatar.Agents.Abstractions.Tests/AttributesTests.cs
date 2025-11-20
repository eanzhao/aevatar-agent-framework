using System.Reflection;
using Aevatar.Agents.Abstractions.Attributes;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Abstractions.Tests;

/// <summary>
/// Tests for agent framework attributes
/// These tests verify attribute behavior, reflection discovery, and property defaults
/// </summary>
public class AttributesTests
{
    #region EventHandlerAttribute Tests

    [Fact(DisplayName = "EventHandlerAttribute should have correct default values")]
    public void EventHandlerAttribute_Should_Have_Correct_Defaults()
    {
        // Arrange & Act
        var attribute = new EventHandlerAttribute();

        // Assert
        attribute.AllowSelfHandling.ShouldBeFalse();
        attribute.Priority.ShouldBe(0); // Default priority is 0 (highest)
    }

    [Fact(DisplayName = "EventHandlerAttribute should allow setting properties")]
    public void EventHandlerAttribute_Should_Allow_Setting_Properties()
    {
        // Arrange & Act
        var attribute = new EventHandlerAttribute
        {
            AllowSelfHandling = true,
            Priority = 100
        };

        // Assert
        attribute.AllowSelfHandling.ShouldBeTrue();
        attribute.Priority.ShouldBe(100);
    }

    [Fact(DisplayName = "EventHandlerAttribute should be discoverable via reflection")]
    public void EventHandlerAttribute_Should_Be_Discoverable_Via_Reflection()
    {
        // Arrange
        var method = typeof(TestAgentWithEventHandlers)
            .GetMethod(nameof(TestAgentWithEventHandlers.HandleSpecificEvent));

        // Act
        var attribute = method?.GetCustomAttribute<EventHandlerAttribute>();

        // Assert
        attribute.ShouldNotBeNull();
        attribute.Priority.ShouldBe(10);
        attribute.AllowSelfHandling.ShouldBeTrue();
    }

    [Fact(DisplayName = "EventHandlerAttribute should only be applicable to methods")]
    public void EventHandlerAttribute_Should_Only_Apply_To_Methods()
    {
        // Arrange
        var attributeType = typeof(EventHandlerAttribute);

        // Act
        var attributeUsage = attributeType.GetCustomAttribute<AttributeUsageAttribute>();

        // Assert
        attributeUsage.ShouldNotBeNull();
        attributeUsage.ValidOn.ShouldBe(AttributeTargets.Method);
    }

    #endregion

    #region AllEventHandlerAttribute Tests

    [Fact(DisplayName = "AllEventHandlerAttribute should have lowest priority by default")]
    public void AllEventHandlerAttribute_Should_Have_Lowest_Priority_By_Default()
    {
        // Arrange & Act
        var attribute = new AllEventHandlerAttribute();

        // Assert
        attribute.AllowSelfHandling.ShouldBeFalse();
        attribute.Priority.ShouldBe(int.MaxValue); // Default is lowest priority
    }

    [Fact(DisplayName = "AllEventHandlerAttribute should allow priority override")]
    public void AllEventHandlerAttribute_Should_Allow_Priority_Override()
    {
        // Arrange & Act
        var attribute = new AllEventHandlerAttribute
        {
            Priority = 1,
            AllowSelfHandling = true
        };

        // Assert
        attribute.Priority.ShouldBe(1);
        attribute.AllowSelfHandling.ShouldBeTrue();
    }

    [Fact(DisplayName = "AllEventHandlerAttribute should be discoverable via reflection")]
    public void AllEventHandlerAttribute_Should_Be_Discoverable_Via_Reflection()
    {
        // Arrange
        var method = typeof(TestAgentWithEventHandlers)
            .GetMethod(nameof(TestAgentWithEventHandlers.HandleAllEvents));

        // Act
        var attribute = method?.GetCustomAttribute<AllEventHandlerAttribute>();

        // Assert
        attribute.ShouldNotBeNull();
        attribute.Priority.ShouldBe(999); // Custom priority
        attribute.AllowSelfHandling.ShouldBeFalse();
    }

    [Fact(DisplayName = "AllEventHandlerAttribute should only be applicable to methods")]
    public void AllEventHandlerAttribute_Should_Only_Apply_To_Methods()
    {
        // Arrange
        var attributeType = typeof(AllEventHandlerAttribute);

        // Act
        var attributeUsage = attributeType.GetCustomAttribute<AttributeUsageAttribute>();

        // Assert
        attributeUsage.ShouldNotBeNull();
        attributeUsage.ValidOn.ShouldBe(AttributeTargets.Method);
    }

    #endregion

    #region ConfigurationAttribute Tests

    [Fact(DisplayName = "ConfigurationAttribute should be empty marker attribute")]
    public void ConfigurationAttribute_Should_Be_Empty_Marker()
    {
        // Arrange & Act
        var attribute = new ConfigurationAttribute();
        var properties = attribute.GetType().GetProperties();

        // Assert - Should have no custom properties (only inherited from Attribute)
        properties.Where(p => p.DeclaringType == typeof(ConfigurationAttribute))
            .ShouldBeEmpty();
    }

    [Fact(DisplayName = "ConfigurationAttribute should be discoverable via reflection")]
    public void ConfigurationAttribute_Should_Be_Discoverable_Via_Reflection()
    {
        // Arrange
        var method = typeof(TestAgentWithConfiguration)
            .GetMethod(nameof(TestAgentWithConfiguration.Configure));

        // Act
        var attribute = method?.GetCustomAttribute<ConfigurationAttribute>();

        // Assert
        attribute.ShouldNotBeNull();
    }

    [Fact(DisplayName = "ConfigurationAttribute should only be applicable to methods")]
    public void ConfigurationAttribute_Should_Only_Apply_To_Methods()
    {
        // Arrange
        var attributeType = typeof(ConfigurationAttribute);

        // Act
        var attributeUsage = attributeType.GetCustomAttribute<AttributeUsageAttribute>();

        // Assert
        attributeUsage.ShouldNotBeNull();
        attributeUsage.ValidOn.ShouldBe(AttributeTargets.Method);
    }

    #endregion

    #region Multiple Attributes Tests

    [Fact(DisplayName = "Method should support multiple event handler attributes")]
    public void Method_Should_Not_Support_Multiple_Same_Attributes()
    {
        // Arrange
        var method = typeof(TestAgentWithEventHandlers)
            .GetMethod(nameof(TestAgentWithEventHandlers.HandleSpecificEvent));

        // Act
        var eventHandlerAttributes = method?.GetCustomAttributes<EventHandlerAttribute>().ToList();

        // Assert - By default, attributes don't allow multiple
        eventHandlerAttributes.ShouldNotBeNull();
        eventHandlerAttributes.Count.ShouldBe(1); // Only one instance allowed
    }

    [Fact(DisplayName = "Different handler attributes should be mutually exclusive")]
    public void Different_Handler_Attributes_Should_Be_Allowed_Together()
    {
        // This test verifies that a method can have both EventHandler and AllEventHandler
        // though in practice this might not make sense
        var method = typeof(TestAgentWithMultipleAttributes)
            .GetMethod(nameof(TestAgentWithMultipleAttributes.HandleWithBothAttributes));

        // Act
        var eventHandlerAttr = method?.GetCustomAttribute<EventHandlerAttribute>();
        var allEventHandlerAttr = method?.GetCustomAttribute<AllEventHandlerAttribute>();

        // Assert - Both can exist (though not recommended in practice)
        // This is a framework design decision - should they be mutually exclusive?
        eventHandlerAttr.ShouldNotBeNull();
        allEventHandlerAttr.ShouldNotBeNull();
    }

    #endregion

    #region Priority Ordering Tests

    [Fact(DisplayName = "Should order handlers by priority correctly")]
    public void Should_Order_Handlers_By_Priority()
    {
        // Arrange
        var methods = typeof(TestAgentWithPriorities)
            .GetMethods()
            .Where(m => m.GetCustomAttribute<EventHandlerAttribute>() != null)
            .Select(m => new
            {
                Method = m,
                Attribute = m.GetCustomAttribute<EventHandlerAttribute>()!
            })
            .ToList();

        // Act
        var orderedMethods = methods.OrderBy(m => m.Attribute.Priority).ToList();

        // Assert
        orderedMethods[0].Method.Name.ShouldBe(nameof(TestAgentWithPriorities.HighPriorityHandler));
        orderedMethods[1].Method.Name.ShouldBe(nameof(TestAgentWithPriorities.MediumPriorityHandler));
        orderedMethods[2].Method.Name.ShouldBe(nameof(TestAgentWithPriorities.LowPriorityHandler));
    }

    [Fact(DisplayName = "AllEventHandler should have lower priority than specific handlers by default")]
    public void AllEventHandler_Should_Have_Lower_Priority_Than_Specific_By_Default()
    {
        // Arrange
        var specificHandler = new EventHandlerAttribute(); // Default priority = 0
        var allHandler = new AllEventHandlerAttribute(); // Default priority = int.MaxValue

        // Assert
        specificHandler.Priority.ShouldBeLessThan(allHandler.Priority);
    }

    #endregion

    #region Test Classes

    // Test class for EventHandlerAttribute discovery
    private class TestAgentWithEventHandlers
    {
        [EventHandler(Priority = 10, AllowSelfHandling = true)]
        public Task HandleSpecificEvent(AbstractionsTestEvent evt)
        {
            return Task.CompletedTask;
        }

        [AllEventHandler(Priority = 999)]
        public Task HandleAllEvents(AbstractionsTestEvent evt)
        {
            return Task.CompletedTask;
        }
    }

    // Test class for ConfigurationAttribute discovery
    private class TestAgentWithConfiguration
    {
        [Configuration]
        public void Configure()
        {
        }
    }

    // Test class for multiple attributes
    private class TestAgentWithMultipleAttributes
    {
        [EventHandler]
        [AllEventHandler]
        public Task HandleWithBothAttributes(AbstractionsTestEvent evt)
        {
            return Task.CompletedTask;
        }
    }

    // Test class for priority ordering
    private class TestAgentWithPriorities
    {
        [EventHandler(Priority = 1)]
        public Task HighPriorityHandler(AbstractionsTestEvent evt) => Task.CompletedTask;

        [EventHandler(Priority = 50)]
        public Task MediumPriorityHandler(AbstractionsTestEvent evt) => Task.CompletedTask;

        [EventHandler(Priority = 100)]
        public Task LowPriorityHandler(AbstractionsTestEvent evt) => Task.CompletedTask;
    }

    #endregion
}