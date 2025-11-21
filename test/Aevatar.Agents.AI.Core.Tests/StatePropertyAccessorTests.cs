using Aevatar.Agents.Core.StateProtection;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.AI.Core.Tests;

public class StatePropertyAccessorTests
{
    [Fact(DisplayName = "StatePropertyAccessor GetValue should return same instance when source is same")]
    public void GetValue_ShouldReturnSameInstance_WhenSourceIsSame()
    {
        // Arrange
        var accessor = new StatePropertyAccessor<StringValue>();
        var config = new StringValue { Value = "initial" };
        var packed = Any.Pack(config);

        // Act
        var value1 = accessor.GetValue(packed);
        var value2 = accessor.GetValue(packed);

        // Assert
        Assert.Same(value1, value2);
    }

    [Fact(DisplayName = "StatePropertyAccessor modifications should be preserved across accesses")]
    public void Modifications_ShouldBePreserved_AcrossAccesses()
    {
        // Arrange
        var accessor = new StatePropertyAccessor<StringValue>();
        var config = new StringValue { Value = "initial" };
        var packed = Any.Pack(config);

        // Act
        var value1 = accessor.GetValue(packed);
        value1.Value = "modified";
        
        var value2 = accessor.GetValue(packed);

        // Assert
        Assert.Equal("modified", value2.Value);
        Assert.Same(value1, value2);
    }

    [Fact(DisplayName = "StatePropertyAccessor SetValue should update cache")]
    public void SetValue_ShouldUpdateCache()
    {
        // Arrange
        using var _ = StateProtectionContext.BeginInitializationScope();
        var accessor = new StatePropertyAccessor<StringValue>();
        var newConfig = new StringValue { Value = "new" };

        // Act
        var packed = accessor.SetValue(newConfig, "test");
        var value = accessor.GetValue(packed);

        // Assert
        Assert.Same(newConfig, value);
    }
}
