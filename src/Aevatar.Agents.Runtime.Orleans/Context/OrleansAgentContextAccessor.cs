using Aevatar.Agents.Abstractions.Context;

namespace Aevatar.Agents.Runtime.Orleans.Context;

/// <summary>
/// Orleans-specific context accessor that bridges with Orleans.Runtime.RequestContext.
/// Provides IAgentContextAccessor interface for Orleans runtime.
/// </summary>
public class OrleansAgentContextAccessor : IAgentContextAccessor
{
    // Thread-local bridge instance to avoid creating multiple instances per request
    private static readonly AsyncLocal<OrleansAgentContextBridge?> BridgeInstance = new();

    /// <inheritdoc />
    public IAgentContext? Context
    {
        get => GetOrCreateBridge();
        set
        {
            // When setting context, sync values to Orleans RequestContext
            if (value != null)
            {
                var bridge = GetOrCreateBridge();
                foreach (var (key, val) in value.GetAll())
                {
                    if (val != null)
                    {
                        bridge.Set(key, val);
                    }
                }
            }
        }
    }

    private static OrleansAgentContextBridge GetOrCreateBridge()
    {
        return BridgeInstance.Value ??= new OrleansAgentContextBridge();
    }

    /// <summary>
    /// Get or create current context.
    /// </summary>
    public IAgentContext GetOrCreate()
    {
        return GetOrCreateBridge();
    }
}

