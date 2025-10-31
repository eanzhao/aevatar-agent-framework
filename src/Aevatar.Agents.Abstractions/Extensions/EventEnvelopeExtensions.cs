namespace Aevatar.Agents.Abstractions;

/// <summary>
/// EventEnvelope 扩展方法
/// </summary>
public static class EventEnvelopeExtensions
{
    /// <summary>
    /// 获取发布时间（UTC）
    /// </summary>
    public static DateTime GetPublishedTimestampUtc(this EventEnvelope envelope)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(envelope.PublishedTimestampUtc).UtcDateTime;
    }

    /// <summary>
    /// 设置发布时间为当前时间（UTC）
    /// </summary>
    public static void SetPublishedTimestampUtcNow(this EventEnvelope envelope)
    {
        envelope.PublishedTimestampUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// 获取事件年龄（从发布到现在的时长）
    /// </summary>
    public static TimeSpan GetEventAge(this EventEnvelope envelope)
    {
        var publishedTime = envelope.GetPublishedTimestampUtc();
        return DateTime.UtcNow - publishedTime;
    }

    /// <summary>
    /// 判断事件是否过期
    /// </summary>
    /// <param name="envelope">事件信封</param>
    /// <param name="maxAge">最大年龄</param>
    /// <returns>是否过期</returns>
    public static bool IsExpired(this EventEnvelope envelope, TimeSpan maxAge)
    {
        return envelope.GetEventAge() > maxAge;
    }
}