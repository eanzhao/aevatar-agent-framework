using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace MakerProjectsDemo.Projects.Paper;

public static class PaperContent
{
    public const string Title = "Solving a Million-Step LLM Task with Zero Errors";
    private const string ArticleFileName = "Solving a Million-Step LLM Task with Zero Errors.html";

    private static readonly Lazy<string> LazyFullText = new(LoadArticleText, LazyThreadSafetyMode.ExecutionAndPublication);
    public static string FullText => LazyFullText.Value;

    private static string LoadArticleText()
    {
        try
        {
            var articlePath = FindArticlePath();
            if (!string.IsNullOrEmpty(articlePath) && File.Exists(articlePath))
            {
                var html = File.ReadAllText(articlePath);
                return ConvertHtmlToPlainText(html);
            }
        }
        catch
        {
            // swallow and fall back to embedded summary
        }

        return "The article file could not be located. Please ensure articles/Solving a Million-Step LLM Task with Zero Errors.html exists.";
    }

    private static string? FindArticlePath()
    {
        var current = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(current); i++)
        {
            var candidate = Path.Combine(current, "articles", ArticleFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(current);
            current = parent?.FullName;
        }

        return null;
    }

    private static string ConvertHtmlToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var sanitized = Regex.Replace(html, @"<(script|style)[^>]*?>.*?</\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        string ReplaceBlockTags(string input, string tag, string replacement)
        {
            return Regex.Replace(input, $@"</?{tag}[^>]*?>", replacement, RegexOptions.IgnoreCase);
        }

        var blockTags = new[] { "p", "div", "section", "article", "br", "li", "ul", "ol", "h1", "h2", "h3", "h4", "h5", "h6", "table", "tr" };
        foreach (var tag in blockTags)
        {
            sanitized = ReplaceBlockTags(sanitized, tag, "\n");
        }

        sanitized = Regex.Replace(sanitized, "<[^>]+>", " ");
        sanitized = WebUtility.HtmlDecode(sanitized);
        sanitized = Regex.Replace(sanitized, @"\r\n|\r", "\n");
        sanitized = Regex.Replace(sanitized, @"[ \t]+", " ");
        sanitized = Regex.Replace(sanitized, @"\n{3,}", "\n\n");
        sanitized = Regex.Replace(sanitized, @" ?\n ?", "\n");

        return sanitized.Trim();
    }
}
