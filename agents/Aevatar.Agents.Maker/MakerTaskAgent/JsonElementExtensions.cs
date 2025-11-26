using System.Text.Json;

namespace Aevatar.Agents.Maker;

internal static class JsonElementExtensions
{
    public static string? GetPropertyOrDefault(this JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return element.TryGetProperty(propertyName, out var prop)
            ? prop.GetString()
            : null;
    }
}

