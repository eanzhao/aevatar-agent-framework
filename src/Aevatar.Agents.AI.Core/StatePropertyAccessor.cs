using System.Diagnostics;
using System.Runtime.CompilerServices;
using Aevatar.Agents.Core.StateProtection;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

[assembly: InternalsVisibleTo("Aevatar.Agents.AI.Core.Tests")]

namespace Aevatar.Agents.AI.Core;

/// <summary>
/// Helper class to encapsulate common State/Config property access logic.
/// </summary>
internal class StatePropertyAccessor<T> where T : class, IMessage<T>, new()
{
    private T _value = new();
    private Any? _lastPackedValue;

    public T GetValue(Any? packedValue, bool checkProtection = true)
    {
#if DEBUG
        if (checkProtection && !StateProtectionContext.IsModifiable)
        {
            var callerMethod = new StackFrame(2).GetMethod()?.Name ?? "Unknown";
            Debug.WriteLine(
                $"WARNING: State/Config accessed from '{callerMethod}' outside protected context. " +
                "Should only be modified within OnActivateAsync or event handlers.");
        }
#endif
        // Return cached value if source hasn't changed
        if (_value != null && object.ReferenceEquals(packedValue, _lastPackedValue))
        {
            return _value;
        }

        _value = packedValue == null ? new T() : packedValue.Unpack<T>();
        _lastPackedValue = packedValue;

        return _value;
    }

    public Any SetValue(T value, string operationName)
    {
        StateProtectionContext.EnsureModifiable(operationName);
        _value = value;
        var packed = Any.Pack(value);
        _lastPackedValue = packed;
        return packed;
    }
}