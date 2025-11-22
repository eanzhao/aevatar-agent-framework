using Aevatar.Agents.Abstractions.Attributes;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Core.Tests.Agents;

/// <summary>
/// Minimal agent with minimal state and config for edge case testing
/// </summary>
public class MinimalGenericAgent : GAgentBase<MinimalState, MinimalConfig>
{
    protected override Task OnActivateAsync(CancellationToken ct = default)
    {
        State.Value = 0;
        return base.OnActivateAsync(ct);
    }
    
    public override string GetDescription()
    {
        return $"MinimalAgent: {State.Value}";
    }

    [EventHandler]
    public async Task ModifyStateAsync(Empty empty)
    {
        State.Value = 42;
    }
}