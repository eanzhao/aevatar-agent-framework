namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 响应格式
/// </summary>
public class AevatarResponseFormat
{
    public string Type { get; set; } = "text";
    public object? Schema { get; set; }
}