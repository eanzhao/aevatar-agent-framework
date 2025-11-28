using Aevatar.Agents.AI.Abstractions;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Maker.Resilience;

// ============================================================
//  Resilient LLM Wrapper
//  Decorates IAevatarLLMProvider with resilience features:
//  - Automatic retry with exponential backoff
//  - Circuit breaker for cascade failure prevention
//  - Telemetry and health monitoring
//
//  Design Philosophy:
//  - Transparent wrapper - drop-in replacement
//  - All failures are transient until proven permanent
//  - Fail fast when circuit is open, recover gracefully
// ============================================================

/// <summary>
/// Resilient wrapper for LLM providers.
/// Adds retry, circuit breaker, and timeout handling.
/// </summary>
public sealed class ResilientLLMWrapper : IAevatarLLMProvider
{
    private readonly IAevatarLLMProvider _inner;
    private readonly LLMResiliencePolicy _policy;
    private readonly string _providerName;
    private readonly ILogger? _logger;

    public ResilientLLMWrapper(
        IAevatarLLMProvider inner,
        string providerName,
        LLMResiliencePolicy policy,
        ILogger? logger = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _providerName = providerName ?? throw new ArgumentNullException(nameof(providerName));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _logger = logger;
    }

    /// <summary>
    /// Generate response with resilience protection.
    /// </summary>
    public async Task<AevatarLLMResponse> GenerateAsync(
        AevatarLLMRequest request,
        CancellationToken cancellationToken = default)
    {
        var outcome = await _policy.ExecuteAsync(
            _providerName,
            async ct => await _inner.GenerateAsync(request, ct),
            cancellationToken);

        if (outcome.Success)
        {
            return outcome.Result!;
        }

        // Log detailed failure info
        _logger?.LogError(outcome.LastException,
            "[RESILIENT] LLM call failed after {Attempts} attempts in {Duration:F1}s. Provider: {Provider}, CircuitBroken: {CircuitBroken}",
            outcome.AttemptsUsed, outcome.TotalDuration.TotalSeconds, _providerName, outcome.WasCircuitBroken);

        // Re-throw the last exception with context
        throw new ResilientLLMException(
            $"LLM call failed after {outcome.AttemptsUsed} attempts",
            _providerName,
            outcome.AttemptsUsed,
            outcome.TotalDuration,
            outcome.WasCircuitBroken,
            outcome.LastException);
    }

    /// <summary>
    /// Generate streaming response with resilience protection.
    /// Note: Retry only applies to initial connection, not mid-stream failures.
    /// </summary>
    public async IAsyncEnumerable<AevatarLLMToken> GenerateStreamAsync(
        AevatarLLMRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // For streaming, we can only retry the initial connection
        // Mid-stream failures must be handled by caller
        IAsyncEnumerable<AevatarLLMToken>? stream = null;

        var outcome = await _policy.ExecuteAsync(
            _providerName,
            async ct =>
            {
                // Get the stream (this is where connection errors occur)
                stream = _inner.GenerateStreamAsync(request, ct);

                // Try to get first token to verify connection
                var enumerator = stream.GetAsyncEnumerator(ct);
                try
                {
                    if (await enumerator.MoveNextAsync())
                    {
                        // Success - return the first token and continue
                        return enumerator.Current;
                    }
                    return new AevatarLLMToken { Content = string.Empty, IsComplete = true };
                }
                finally
                {
                    // We need to keep the enumerator alive for the streaming phase
                    // This is handled by returning the stream below
                }
            },
            cancellationToken);

        if (!outcome.Success)
        {
            _logger?.LogError(outcome.LastException,
                "[RESILIENT] Stream initiation failed after {Attempts} attempts. Provider: {Provider}",
                outcome.AttemptsUsed, _providerName);

            throw new ResilientLLMException(
                "Stream initiation failed",
                _providerName,
                outcome.AttemptsUsed,
                outcome.TotalDuration,
                outcome.WasCircuitBroken,
                outcome.LastException);
        }

        // Yield the first token if we got one
        if (outcome.Result != null && !string.IsNullOrEmpty(outcome.Result.Content))
        {
            yield return outcome.Result;
        }

        // Continue streaming from inner provider
        if (stream != null)
        {
            await foreach (var token in stream.WithCancellation(cancellationToken))
            {
                yield return token;
            }
        }
    }

    /// <summary>
    /// Get provider health status.
    /// </summary>
    public ProviderHealthStatus GetHealthStatus()
    {
        var allStatus = _policy.GetHealthStatus();
        if (allStatus.TryGetValue(_providerName, out var status))
        {
            return status;
        }

        return new ProviderHealthStatus
        {
            ProviderName = _providerName,
            State = CircuitState.Closed
        };
    }
}

/// <summary>
/// Exception thrown when resilient LLM call exhausts all retries.
/// </summary>
public sealed class ResilientLLMException : Exception
{
    public string ProviderName { get; }
    public int AttemptsUsed { get; }
    public TimeSpan TotalDuration { get; }
    public bool WasCircuitBroken { get; }

    public ResilientLLMException(
        string message,
        string providerName,
        int attemptsUsed,
        TimeSpan totalDuration,
        bool wasCircuitBroken,
        Exception? innerException)
        : base(message, innerException)
    {
        ProviderName = providerName;
        AttemptsUsed = attemptsUsed;
        TotalDuration = totalDuration;
        WasCircuitBroken = wasCircuitBroken;
    }
}

/// <summary>
/// Factory for creating resilient LLM wrappers.
/// </summary>
public static class ResilientLLMFactory
{
    /// <summary>
    /// Wrap an LLM provider with resilience features.
    /// </summary>
    public static ResilientLLMWrapper WrapWithResilience(
        IAevatarLLMProvider provider,
        string providerName,
        MakerOptions options,
        ILogger? logger = null)
    {
        var config = new LLMResilienceConfig
        {
            MaxRetries = options.MaxRetries,
            InitialRetryDelay = options.InitialRetryDelay,
            MaxRetryDelay = options.MaxRetryDelay,
            CircuitBreakerThreshold = options.CircuitBreakerThreshold,
            CircuitBreakerDuration = options.CircuitBreakerDuration,
            EnableJitter = true,
            CallTimeout = options.StepTimeout
        };

        var policy = new LLMResiliencePolicy(config, logger);

        return new ResilientLLMWrapper(provider, providerName, policy, logger);
    }
}

