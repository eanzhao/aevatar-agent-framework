namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 验证结果
/// </summary>
public class AevatarValidationResult
{
    /// <summary>
    /// 是否有效
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// 错误列表
    /// </summary>
    public IList<AevatarValidationError> Errors { get; set; } = new List<AevatarValidationError>();
}