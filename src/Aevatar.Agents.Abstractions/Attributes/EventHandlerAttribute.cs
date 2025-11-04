namespace Aevatar.Agents.Abstractions;

/// <summary>
/// 标记事件处理方法
/// 被标记的方法会自动注册为事件处理器
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class EventHandlerAttribute : Attribute
{
    /// <summary>
    /// 处理器优先级（数字越小优先级越高）
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// 是否允许处理自己发出的事件
    /// </summary>
    public bool AllowSelfHandling { get; set; } = false;
}
