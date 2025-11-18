using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Abstractions.Helpers;

public static class TimestampHelper
{
    public static Timestamp GetUtcNow()
    {
        return Timestamp.FromDateTime(DateTime.UtcNow);
    }
}