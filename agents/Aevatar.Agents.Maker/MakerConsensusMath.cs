using System.Security.Cryptography;
using System.Text;

namespace Aevatar.Agents.Maker;

internal static class MakerConsensusMath
{
    public static string Canonicalize(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        return content.Trim();
    }

    public static string ComputeHash(string canonicalContent)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(canonicalContent);
        var hashBytes = sha.ComputeHash(bytes);
        return Convert.ToHexString(hashBytes);
    }

    public static string BuildPreview(string? content, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "[empty]";
        }

        var normalized = content.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength] + "...";
    }
}