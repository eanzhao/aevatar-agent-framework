using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Providers;

namespace Aevatar.Agents.Maker.V2;

// ============================================================
//  Execution Pool - Parallel LLM Invocation with Decorrelation
//  Directly uses IAevatarLLMProvider - no unnecessary abstraction
// ============================================================

/// <summary>
/// Execution pool that manages parallel LLM calls with decorrelation support.
/// Directly uses Aevatar's IAevatarLLMProvider.
/// </summary>
public sealed class ExecutionPool
{
    private readonly IReadOnlyList<IAevatarLLMProvider> _providers;
    private readonly float _temperatureVariance;
    private int _nextProviderIndex;

    /// <summary>
    /// Create an execution pool with multiple providers for decorrelation.
    /// </summary>
    public ExecutionPool(IReadOnlyList<IAevatarLLMProvider> providers, float temperatureVariance = 0.1f)
    {
        if (providers.Count == 0)
            throw new ArgumentException("At least one provider is required.", nameof(providers));

        _providers = providers;
        _temperatureVariance = temperatureVariance;
    }

    /// <summary>
    /// Create an execution pool with a single provider.
    /// </summary>
    public ExecutionPool(IAevatarLLMProvider provider) : this([provider]) { }

    /// <summary>
    /// Create an execution pool from ILLMProviderFactory.
    /// </summary>
    public ExecutionPool(ILLMProviderFactory factory, int poolSize = 3, float temperatureVariance = 0.1f)
        : this(CreateProviders(factory, poolSize), temperatureVariance) { }

    private static List<IAevatarLLMProvider> CreateProviders(ILLMProviderFactory factory, int count)
    {
        var providers = new List<IAevatarLLMProvider>(count);
        for (var i = 0; i < count; i++)
        {
            providers.Add(factory.GetDefaultProvider());
        }
        return providers;
    }

    /// <summary>
    /// Execute multiple requests in parallel, yielding results as they complete.
    /// Remaining tasks are gracefully cancelled when the caller stops enumerating.
    /// </summary>
    public async IAsyncEnumerable<(string Content, bool Success, string? Error)> ExecuteParallelAsync(
        string systemPrompt,
        string userPrompt,
        int count,
        float temperature = 0.2f,
        int maxTokens = 1024,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        // Use a linked CTS so we can cancel remaining tasks when caller stops enumerating
        using var batchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var batchToken = batchCts.Token;

        var tasks = new List<Task<(string, bool, string?)>>(count);
        for (var i = 0; i < count; i++)
        {
            var provider = GetNextProvider();
            var temp = DecorrelateTemperature(temperature, i);
            tasks.Add(ExecuteSingleAsync(provider, systemPrompt, userPrompt, temp, maxTokens, batchToken));
        }

        try
        {
            while (tasks.Count > 0)
            {
                var completedTask = await Task.WhenAny(tasks);
                tasks.Remove(completedTask);

                // Don't await if caller's token was cancelled
                if (ct.IsCancellationRequested)
                    yield break;

                yield return await completedTask;
            }
        }
        finally
        {
            // Cancel remaining tasks gracefully when caller stops enumerating early
            await batchCts.CancelAsync();

            // Wait for remaining tasks to complete (they will be cancelled)
            // This prevents TaskCanceledException from bubbling up
            try
            {
                await Task.WhenAll(tasks);
            }
            catch
            {
                // Swallow cancellation exceptions - this is expected behavior
            }
        }
    }

    /// <summary>
    /// Execute a single request.
    /// </summary>
    public Task<(string Content, bool Success, string? Error)> ExecuteAsync(
        string systemPrompt,
        string userPrompt,
        float temperature = 0.2f,
        int maxTokens = 1024,
        CancellationToken ct = default)
    {
        return ExecuteSingleAsync(GetNextProvider(), systemPrompt, userPrompt, temperature, maxTokens, ct);
    }

    private async Task<(string, bool, string?)> ExecuteSingleAsync(
        IAevatarLLMProvider provider,
        string systemPrompt,
        string userPrompt,
        float temperature,
        int maxTokens,
        CancellationToken ct)
    {
        try
        {
            var request = new AevatarLLMRequest
            {
                SystemPrompt = systemPrompt,
                Settings = new AevatarLLMSettings
                {
                    Temperature = temperature,
                    MaxTokens = maxTokens
                },
                Messages = [new AevatarChatMessage { Role = AevatarChatRole.User, Content = userPrompt }]
            };

            var response = await provider.GenerateAsync(request, ct);
            return (response.Content ?? string.Empty, true, null);
        }
        catch (OperationCanceledException)
        {
            // Expected when batch is cancelled - don't log as error
            return (string.Empty, false, null);
        }
        catch (Exception ex)
        {
            return (string.Empty, false, ex.Message);
        }
    }

    private IAevatarLLMProvider GetNextProvider()
    {
        var index = Interlocked.Increment(ref _nextProviderIndex) % _providers.Count;
        return _providers[index];
    }

    private float DecorrelateTemperature(float baseTemp, int index)
    {
        var variance = (_temperatureVariance * index) - (_temperatureVariance * 0.5f);
        return Math.Clamp(baseTemp + variance, 0.0f, 2.0f);
    }
}

/// <summary>
/// Extension method to create ExecutionPool from ILLMProviderFactory.
/// </summary>
public static class ExecutionPoolExtensions
{
    public static ExecutionPool CreateExecutionPool(
        this ILLMProviderFactory factory,
        int poolSize = 3,
        float temperatureVariance = 0.1f)
    {
        return new ExecutionPool(factory, poolSize, temperatureVariance);
    }
}
