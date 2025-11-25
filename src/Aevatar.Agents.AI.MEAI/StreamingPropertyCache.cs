using System.Collections.Concurrent;
using System.Reflection;

namespace Aevatar.Agents.AI.MEAI;

internal static class StreamingPropertyCache
{
    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> Cache = new();

    public static PropertyInfo? GetProperty(Type? type, string propertyName)
    {
        if (type == null || string.IsNullOrEmpty(propertyName))
        {
            return null;
        }

        return Cache.GetOrAdd((type, propertyName), static key => key.Item1.GetProperty(key.Item2));
    }

    public static object? GetValue(object? obj, string propertyName)
    {
        if (obj == null)
        {
            return null;
        }

        var prop = GetProperty(obj.GetType(), propertyName);
        return prop?.GetValue(obj);
    }
}