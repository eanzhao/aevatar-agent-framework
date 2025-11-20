using Aevatar.Agents.Abstractions.Attributes;

namespace Aevatar.Agents.Core.Tests.Agents;

/// <summary>
/// Test agent for exception handling
/// </summary>
public class ExceptionTestAgent : GAgentBase<TestAgentState>
{
    public bool ThrowInHandler { get; set; }
    public bool ThrowInActivate { get; set; }
    public bool ThrowInDeactivate { get; set; }
    public int SuccessfulHandlerCount { get; private set; }
    public int HandlerExceptionCount { get; private set; }
    
    public override string GetDescription() => "ExceptionTestAgent";
    
    protected override Task OnActivateAsync(CancellationToken ct = default)
    {
        if (ThrowInActivate)
        {
            throw new InvalidOperationException("Activation failed");
        }
        return base.OnActivateAsync(ct);
    }
    
    protected override Task OnDeactivateAsync(CancellationToken ct = default)
    {
        if (ThrowInDeactivate)
        {
            throw new InvalidOperationException("Deactivation failed");
        }
        return base.OnDeactivateAsync(ct);
    }
    
    [EventHandler(Priority = 1)]
    public async Task ThrowingHandler(TestEvent evt)
    {
        if (ThrowInHandler)
        {
            HandlerExceptionCount++;
            throw new InvalidOperationException("Handler failed");
        }
        await Task.CompletedTask;
    }
    
    [EventHandler(Priority = 2)]
    public async Task SuccessfulHandler(TestEvent evt)
    {
        SuccessfulHandlerCount++;
        State.Counter++;
        await Task.CompletedTask;
    }
}