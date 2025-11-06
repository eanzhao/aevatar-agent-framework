namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 函数调用
/// </summary>
public class AevatarFunctionCall
{
    public string Name { get; set; } = string.Empty;
    public string Arguments { get; set; } = "{}";
}