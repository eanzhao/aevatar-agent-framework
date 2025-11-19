namespace Aevatar.Agents.AI.Abstractions.Tests.PromptManager;

/// <summary>
/// Mock implementation of prompt manager for testing
/// </summary>
public class MockPromptManager : IAevatarPromptManager
{
    private readonly Dictionary<string, string> _systemPrompts = new();
    private string _defaultPrompt = "Default system prompt";

    public bool SimulateDelay { get; set; }

    public void AddSystemPrompt(string key, string prompt)
    {
        _systemPrompts[key] = prompt;
    }

    public void SetDefaultPrompt(string prompt)
    {
        _defaultPrompt = prompt;
    }

    public async Task<string> GetSystemPromptAsync(string? key = null, CancellationToken cancellationToken = default)
    {
        if (SimulateDelay)
            await Task.Delay(100, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(key))
            return _defaultPrompt;

        return _systemPrompts.TryGetValue(key, out var prompt) ? prompt : _defaultPrompt;
    }

    public async Task<string> FormatPromptAsync(
        string template,
        Dictionary<string, object>? variables = null,
        CancellationToken cancellationToken = default)
    {
        if (SimulateDelay)
            await Task.Delay(100, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (variables == null)
            return template;

        var result = template;
        foreach (var kvp in variables)
        {
            result = result.Replace($"{{{{{kvp.Key}}}}}", kvp.Value?.ToString() ?? "");
        }

        return await Task.FromResult(result);
    }

    public async Task<IList<AevatarChatMessage>> BuildChatPromptAsync(
        string systemPrompt,
        IList<AevatarChatMessage>? history = null,
        CancellationToken cancellationToken = default)
    {
        if (SimulateDelay)
            await Task.Delay(100, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var messages = new List<AevatarChatMessage>
        {
            new() { Role = AevatarChatRole.System, Content = systemPrompt }
        };

        if (history != null)
        {
            messages.AddRange(history);
        }

        return messages;
    }

    public Task<string> FormatTemplateAsync(
        AevatarPromptTemplate template,
        Dictionary<string, object> variables,
        CancellationToken cancellationToken = default)
    {
        return FormatPromptAsync(template.Content, variables, cancellationToken);
    }
}