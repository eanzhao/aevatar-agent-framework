namespace Aevatar.Agents.Core.Tests.Agents;

/// <summary>
/// Test agent acting as parent
/// </summary>
public class ParentTestAgent : GAgentBase<TestAgentState>
{
    public List<Guid> Children { get; } = new();

    public override string GetDescription() => "ParentTestAgent";

    public async Task AddChildAsync(Guid childId)
    {
        Children.Add(childId);
        await Task.CompletedTask;
    }

    public async Task RemoveChildAsync(Guid childId)
    {
        Children.Remove(childId);
        await Task.CompletedTask;
    }
}