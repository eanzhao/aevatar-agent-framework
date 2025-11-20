using Aevatar.Agents.Abstractions.Helpers;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Abstractions;

/// <summary>
/// EventEnvelope 扩展方法
/// </summary>
public static class EventEnvelopeExtensions
{
    /// <summary>
    /// 获取发布时间（UTC）
    /// </summary>
    public static Timestamp GetPublishedTimestamp(this EventEnvelope envelope)
    {
        return envelope.Timestamp;
    }

    /// <summary>
    /// 设置发布时间为当前时间（UTC）
    /// </summary>
    public static void SetPublishedTimestampToNow(this EventEnvelope envelope)
    {
        envelope.Timestamp = TimestampHelper.GetUtcNow();
    }

    /// <summary>
    /// 获取事件年龄（从发布到现在的时长）
    /// </summary>
    public static Duration GetEventAge(this EventEnvelope envelope)
    {
        var publishedTime = envelope.GetPublishedTimestamp();
        return TimestampHelper.GetUtcNow() - publishedTime;
    }

    /// <summary>
    /// 判断事件是否过期
    /// </summary>
    /// <param name="envelope">事件信封</param>
    /// <param name="maxAge">最大年龄</param>
    /// <returns>是否过期</returns>
    public static bool IsExpired(this EventEnvelope envelope, Duration maxAge)
    {
        return envelope.GetEventAge().Seconds > maxAge.Seconds;
    }
}