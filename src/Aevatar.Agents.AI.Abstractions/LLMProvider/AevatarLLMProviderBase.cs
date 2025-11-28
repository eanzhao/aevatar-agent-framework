using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Abstractions;

// ============================================================
//  Aevatar LLM Provider Base
//  Base class for all LLM providers with built-in resilience:
//  - Automatic retry with exponential backoff
//  - Circuit breaker for cascade failure prevention
//  - Timeout handling
//
//  Design Philosophy:
//  - Resilience is not optional, it's the baseline
//  - Subclasses implement pure API calls, Base handles chaos
//  - Fail fast when circuit is open, recover gracefully
// ============================================================

/// <summary>
/// Base class for LLM providers with built-in resilience.
/// Subclasses implement core API calls; this class wraps them with retry and circuit breaker.
/// </summary>
public abstract class AevatarLLMProviderBase : IAevatarLLMProvider
{
    // ============================================================
    //  Configuration
    // ============================================================

    /// <summary>
    /// Call policy for retry and circuit breaker behavior.
    /// Override in subclass constructor to customize.
    /// </summary>
    protected virtual LLMCallPolicy Policy => LLMCallPolicy.Default;

    /// <summary>
    /// Logger for resilience events. Optional but recommended.
    /// </summary>
    protected virtual ILogger? Logger => null;

    /// <summary>
    /// Provider name for circuit breaker state isolation.
    /// Default is type name; override for custom naming.
    /// </summary>
    protected virtual string ProviderName => GetType().Name;

    // ============================================================
    //  Circuit Breaker State (Static - shared across instances)
    // ============================================================

    private static readonly Dictionary<string, CircuitBreakerState> CircuitBreakers = new();
    private static readonly object CbLock = new();
    private static readonly Random Jitter = new();

    // ============================================================
    //  Abstract Core Methods (Subclasses implement these)
    // ============================================================

    /// <summary>
    /// Core generation logic without resilience wrapping.
    /// Implement the actual API call here.
    /// </summary>
    protected abstract Task<AevatarLLMResponse> GenerateCoreAsync(
        AevatarLLMRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Core streaming generation logic without resilience wrapping.
    /// Implement the actual streaming API call here.
    /// </summary>
    protected abstract IAsyncEnumerable<AevatarLLMToken> GenerateStreamCoreAsync(
        AevatarLLMRequest request,
        CancellationToken cancellationToken);

    // ============================================================
    //  Public Interface (With resilience)
    // ============================================================

    /// <summary>
    /// Generate response with automatic retry and circuit breaker protection.
    /// </summary>
    public async Task<AevatarLLMResponse> GenerateAsync(
        AevatarLLMRequest request,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithPolicyAsync(
            ct => GenerateCoreAsync(request, ct),
            cancellationToken);
    }

    /// <summary>
    /// Generate streaming response with resilience on initial connection.
    /// Note: Retry only applies to initial connection, not mid-stream failures.
    /// </summary>
    public async IAsyncEnumerable<AevatarLLMToken> GenerateStreamAsync(
        AevatarLLMRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // For streaming, we can only retry the initial connection
        IAsyncEnumerable<AevatarLLMToken>? stream = null;
        AevatarLLMToken? firstToken = null;

        await ExecuteWithPolicyAsync(
            async ct =>
            {
                stream = GenerateStreamCoreAsync(request, ct);
                var enumerator = stream.GetAsyncEnumerator(ct);
                try
                {
                    if (await enumerator.MoveNextAsync())
                    {
                        firstToken = enumerator.Current;
                    }
                }
                finally
                {
                    // Enumerator will be disposed when stream iteration completes
                }
                return true; // Dummy return for ExecuteWithPolicyAsync
            },
            cancellationToken);

        // Yield first token if we got one
        if (firstToken != null)
        {
            yield return firstToken;
        }

        // Continue streaming
        if (stream != null)
        {
            var skipFirst = firstToken != null;
            await foreach (var token in stream.WithCancellation(cancellationToken))
            {
                if (skipFirst)
                {
                    skipFirst = false;
                    continue;
                }
                yield return token;
            }
        }
    }

    /// <summary>
    /// Get current health status of this provider.
    /// </summary>
    public ProviderHealthStatus GetHealthStatus()
    {
        var state = GetCircuitBreakerState(ProviderName);
        return new ProviderHealthStatus
        {
            ProviderName = ProviderName,
            State = state.State,
            FailureCount = state.FailureCount,
            LastSuccess = state.LastSuccess,
            LastFailure = state.LastFailure,
            OpenUntil = state.OpenUntil
        };
    }

    // ============================================================
    //  Helper Methods (For subclass use)
    // ============================================================

    /// <summary>
    /// Map Aevatar function parameters to provider-specific format.
    /// Default implementation creates a JSON schema object.
    /// </summary>
    protected virtual object MapFunctionParameters(Dictionary<string, AevatarParameterDefinition> parameters)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var param in parameters)
        {
            var paramDef = new Dictionary<string, object>
            {
                ["type"] = param.Value.Type,
                ["description"] = param.Value.Description ?? ""
            };

            if (param.Value.Enum != null && param.Value.Enum.Count > 0)
            {
                paramDef["enum"] = param.Value.Enum;
            }

            properties[param.Key] = paramDef;

            if (param.Value.Required)
            {
                required.Add(param.Key);
            }
        }

        return new
        {
            type = "object",
            properties = properties,
            required = required
        };
    }

    /// <summary>
    /// Create AevatarTokenUsage from provider-specific token counts.
    /// </summary>
    protected virtual AevatarTokenUsage? CreateTokenUsage(int? promptTokens, int? completionTokens, int? totalTokens)
    {
        if (promptTokens == null && completionTokens == null && totalTokens == null)
            return null;

        return new AevatarTokenUsage
        {
            PromptTokens = promptTokens ?? 0,
            CompletionTokens = completionTokens ?? 0,
            TotalTokens = totalTokens ?? (promptTokens ?? 0) + (completionTokens ?? 0)
        };
    }

    // ============================================================
    //  Resilience Implementation
    // ============================================================

    private async Task<T> ExecuteWithPolicyAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct)
    {
        var policy = Policy;
        var startTime = DateTime.UtcNow;
        var attempts = 0;
        Exception? lastException = null;

        // Check circuit breaker first
        var cbState = GetCircuitBreakerState(ProviderName);
        if (cbState.State == CircuitState.Open)
        {
            if (DateTime.UtcNow < cbState.OpenUntil)
            {
                Logger?.LogWarning(
                    "[LLM] Circuit OPEN for {Provider}, rejecting (opens in {Remaining:F1}s)",
                    ProviderName, (cbState.OpenUntil - DateTime.UtcNow).TotalSeconds);

                throw new CircuitBreakerOpenException(ProviderName, cbState.OpenUntil);
            }

            // Try half-open
            TransitionCircuitBreaker(ProviderName, CircuitState.HalfOpen);
            Logger?.LogInformation("[LLM] Circuit HALF-OPEN for {Provider}, testing...", ProviderName);
        }

        // Retry loop
        while (attempts < policy.MaxRetries)
        {
            attempts++;
            ct.ThrowIfCancellationRequested();

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(policy.CallTimeout);

                var result = await operation(timeoutCts.Token);

                // Success - reset circuit breaker
                OnSuccess(ProviderName);
                return result;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // External cancellation - don't retry
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                OnFailure(ProviderName, ex);

                if (!ShouldRetry(ex, attempts, policy))
                {
                    Logger?.LogWarning(ex,
                        "[LLM] Non-retryable failure for {Provider} on attempt {Attempt}",
                        ProviderName, attempts);
                    break;
                }

                var delay = CalculateDelay(attempts, policy);
                Logger?.LogWarning(
                    "[LLM] Transient failure for {Provider} on attempt {Attempt}/{Max}, retrying in {Delay:F1}s. Error: {Error}",
                    ProviderName, attempts, policy.MaxRetries, delay.TotalSeconds, ex.Message);

                await Task.Delay(delay, ct);
            }
        }

        // All retries exhausted
        throw new LLMCallException(
            $"LLM call failed after {attempts} attempts",
            ProviderName,
            attempts,
            DateTime.UtcNow - startTime,
            cbState.State == CircuitState.Open,
            lastException);
    }

    private static bool ShouldRetry(Exception ex, int currentAttempt, LLMCallPolicy policy)
    {
        if (currentAttempt >= policy.MaxRetries)
            return false;

        return ex switch
        {
            // Timeouts are always retryable
            OperationCanceledException => true,

            // HTTP transient errors
            HttpRequestException hre => IsTransientHttpError(hre),

            // Rate limiting (429) - always retry with backoff
            _ when ex.Message.Contains("429") ||
                   ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) => true,

            // Server errors (5xx)
            _ when ex.Message.Contains("500") || ex.Message.Contains("502") ||
                   ex.Message.Contains("503") || ex.Message.Contains("504") => true,

            // Network errors
            _ when ex.Message.Contains("network", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) => true,

            // Non-retryable: authentication, validation, etc.
            _ => false
        };
    }

    private static bool IsTransientHttpError(HttpRequestException ex)
    {
        return ex.Message.Contains("429") ||
               ex.Message.Contains("500") ||
               ex.Message.Contains("502") ||
               ex.Message.Contains("503") ||
               ex.Message.Contains("504");
    }

    private static TimeSpan CalculateDelay(int attempt, LLMCallPolicy policy)
    {
        var delay = policy.InitialRetryDelay.TotalMilliseconds *
                    Math.Pow(policy.BackoffMultiplier, attempt - 1);

        delay = Math.Min(delay, policy.MaxRetryDelay.TotalMilliseconds);

        if (policy.EnableJitter)
        {
            var jitter = delay * 0.25 * (Jitter.NextDouble() * 2 - 1);
            delay += jitter;
        }

        return TimeSpan.FromMilliseconds(delay);
    }

    // ============================================================
    //  Circuit Breaker State Management
    // ============================================================

    private static CircuitBreakerState GetCircuitBreakerState(string providerName)
    {
        lock (CbLock)
        {
            if (!CircuitBreakers.TryGetValue(providerName, out var state))
            {
                state = new CircuitBreakerState();
                CircuitBreakers[providerName] = state;
            }
            return state;
        }
    }

    private static void TransitionCircuitBreaker(string providerName, CircuitState newState)
    {
        lock (CbLock)
        {
            if (!CircuitBreakers.TryGetValue(providerName, out var state))
            {
                state = new CircuitBreakerState();
                CircuitBreakers[providerName] = state;
            }
            state.State = newState;
        }
    }

    private void OnSuccess(string providerName)
    {
        lock (CbLock)
        {
            if (CircuitBreakers.TryGetValue(providerName, out var state))
            {
                if (state.State == CircuitState.HalfOpen)
                {
                    Logger?.LogInformation(
                        "[LLM] Circuit CLOSED for {Provider} after successful test", providerName);
                }
                state.State = CircuitState.Closed;
                state.FailureCount = 0;
                state.LastSuccess = DateTime.UtcNow;
            }
        }
    }

    private void OnFailure(string providerName, Exception ex)
    {
        var policy = Policy;
        lock (CbLock)
        {
            if (!CircuitBreakers.TryGetValue(providerName, out var state))
            {
                state = new CircuitBreakerState();
                CircuitBreakers[providerName] = state;
            }

            state.FailureCount++;
            state.LastFailure = DateTime.UtcNow;
            state.LastException = ex;

            if (state.FailureCount >= policy.CircuitBreakerThreshold && state.State != CircuitState.Open)
            {
                state.State = CircuitState.Open;
                state.OpenUntil = DateTime.UtcNow + policy.CircuitBreakerDuration;

                Logger?.LogError(
                    "[LLM] Circuit OPENED for {Provider} after {Count} failures. Will test at {OpenUntil:HH:mm:ss}",
                    providerName, state.FailureCount, state.OpenUntil);
            }
        }
    }

    // ============================================================
    //  Internal State Class
    // ============================================================

    private sealed class CircuitBreakerState
    {
        public CircuitState State { get; set; } = CircuitState.Closed;
        public int FailureCount { get; set; }
        public DateTime? LastSuccess { get; set; }
        public DateTime? LastFailure { get; set; }
        public DateTime OpenUntil { get; set; }
        public Exception? LastException { get; set; }
    }
}
