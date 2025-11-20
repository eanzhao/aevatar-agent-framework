namespace Aevatar.Agents.AI.Abstractions.Tests.PromptManager;

/// <summary>
/// Extensions for testing prompt templates
/// </summary>
public static class PromptTemplateExtensions
{
    public static AevatarPromptValidationResult Validate(this AevatarPromptTemplate template)
    {
        var result = new AevatarPromptValidationResult { IsValid = true };

        // Check for unclosed variables
        var openCount = template.Content.Count(c => c == '{');
        var closeCount = template.Content.Count(c => c == '}');

        if (openCount != closeCount)
        {
            result.IsValid = false;
            result.Errors.Add("Unclosed variable brackets");
        }

        return result;
    }

    public static string FormatWithExamples(this AevatarPromptTemplate template, string task)
    {
        var examples = string.Join("\n",
            template.AevatarExamples?.Select(e => $"Input: {e.Input}\nOutput: {e.Output}") ??
            Enumerable.Empty<string>());

        return template.Content
            .Replace("{{task}}", task)
            .Replace("{{examples}}", examples);
    }

    public static string BuildPrompt(this AevatarPromptTemplate template, Dictionary<string, object> variables)
    {
        var prompt = template.Content;

        foreach (var kvp in variables)
        {
            prompt = prompt.Replace($"{{{{{kvp.Key}}}}}", kvp.Value?.ToString() ?? "");
        }

        if (template.AevatarOutputFormat?.Type == "json")
        {
            prompt += "\n\nPlease respond in JSON format.";
        }

        return prompt;
    }
}