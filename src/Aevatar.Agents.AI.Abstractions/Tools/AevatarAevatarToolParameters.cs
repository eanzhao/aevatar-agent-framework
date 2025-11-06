namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// 工具参数定义
/// </summary>
public class AevatarAevatarToolParameters
{
    /// <summary>
    /// 参数字典
    /// </summary>
    public Dictionary<string, AevatarToolParameter> Items { get; set; } = new();
    
    /// <summary>
    /// 必需参数列表
    /// </summary>
    public IList<string> Required { get; set; } = new List<string>();
    
    /// <summary>
    /// 索引器
    /// </summary>
    public AevatarToolParameter this[string name]
    {
        get => Items[name];
        set => Items[name] = value;
    }
}