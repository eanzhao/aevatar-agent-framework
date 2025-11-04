namespace Aevatar.Agents.Abstractions;

/// <summary>
/// 标记处理所有事件的方法（通常用于转发）
/// 方法签名必须是：Task HandleAsync(EventEnvelope envelope)
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class AllEventHandlerAttribute : Attribute
{
    /// <summary>
    /// 是否允许处理自己发出的事件
    /// </summary>
    public bool AllowSelfHandling { get; set; } = false;

    /// <summary>
    /// 处理器优先级（数字越小优先级越高）
    /// </summary>
    public int Priority { get; set; } = int.MaxValue; // 默认最低优先级
}