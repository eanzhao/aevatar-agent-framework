namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 函数定义（用于Function Calling）
/// </summary>
public class AevatarFunctionDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, AevatarParameterDefinition> Parameters { get; set; } = new();
    public bool Required { get; set; }
}