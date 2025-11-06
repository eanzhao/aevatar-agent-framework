namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 验证错误
/// </summary>
public class AevatarValidationError
{
    /// <summary>
    /// 参数名称
    /// </summary>
    public string? ParameterName { get; set; }
    
    /// <summary>
    /// 错误消息
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// 错误代码
    /// </summary>
    public string? ErrorCode { get; set; }
}