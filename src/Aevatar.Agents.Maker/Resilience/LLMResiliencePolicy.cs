using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Maker.Resilience;

// ============================================================
//  LLM Resilience Policy
//  Implements retry with exponential backoff, circuit breaker,
//  and intelligent failure recovery for LLM API calls.
//
//  Design Philosophy:
//  - Network is unreliable, prepare for failures
//  - Exponential backoff respects rate limits
//  - Circuit breaker prevents cascade failures
//  - Every failure is a learning opportunity
// ============================================================

/// <summary>
/// Configuration for LLM resilience policy.
/// </summary>
public sealed record LLMResilienceConfig
{
    /// <summary>Maximum retry attempts for transient failures.</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>Initial delay before first retry (exponentially increases).</summary>
    public TimeSpan InitialRetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Maximum delay between retries.</summary>
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Backoff multiplier for exponential delay.</summary>
    public double BackoffMultiplier { get; init; } = 2.0;

    /// <summary>Add jitter to prevent thundering herd.</summary>
    public bool EnableJitter { get; init; } = true;

    /// <summary>Number of failures before circuit opens.</summary>
    public int CircuitBreakerThreshold { get; init; } = 5;

    /// <summary>Duration to keep circuit open before testing.</summary>
    public TimeSpan CircuitBreakerDuration { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Timeout for individual LLM calls.</summary>
    public TimeSpan CallTimeout { get; init; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// Outcome of a resilient operation.
/// </summary>
public sealed record ResilienceOutcome<T>
{
    public bool Success { get; init; }
    public T? Result { get; init; }
    public Exception? LastException { get; init; }
    public int AttemptsUsed { get; init; }
    public TimeSpan TotalDuration { get; init; }
    public bool WasCircuitBroken { get; init; }
}

/// <summary>
/// Circuit breaker states.
/// </summary>
public enum CircuitState
{
    Closed,     // Normal operation
    Open,       // Rejecting all requests
    HalfOpen    // Testing if service recovered
}

/// <summary>
/// LLM Resilience Policy - wraps LLM calls with retry, circuit breaker, and timeout.
/// Thread-safe for concurrent use across multiple workers.
/// </summary>
public sealed class LLMResiliencePolicy
{
    private readonly LLMResilienceConfig _config;
    private readonly ILogger? _logger;
    private readonly Random _random = new();

    // Circuit breaker state (per provider)
    private readonly Dictionary<string, CircuitBreakerState> _circuitBreakers = new();
    private readonly object _cbLock = new();

    public LLMResiliencePolicy(LLMResilienceConfig? config = null, ILogger? logger = null)
    {
        _config = config ?? new LLMResilienceConfig();
        _logger = logger;
    }

    /// <summary>
    /// Execute an LLM operation with full resilience protection.
    /// </summary>
    public async Task<ResilienceOutcome<T>> ExecuteAsync<T>(
        string providerName,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        var attempts = 0;
        Exception? lastException = null;

        // Check circuit breaker first
        var cbState = GetCircuitBreakerState(providerName);
        if (cbState.State == CircuitState.Open)
        {
            if (DateTime.UtcNow < cbState.OpenUntil)
            {
                _logger?.LogWarning(
                    "[RESILIENCE] Circuit OPEN for provider {Provider}, rejecting request (opens in {Remaining:F1}s)",
                    providerName, (cbState.OpenUntil - DateTime.UtcNow).TotalSeconds);

                return new ResilienceOutcome<T>
                {
                    Success = false,
                    LastException = new CircuitBreakerOpenException(providerName, cbState.OpenUntil),
                    AttemptsUsed = 0,
                    TotalDuration = DateTime.UtcNow - startTime,
                    WasCircuitBroken = true
                };
            }

            // Try half-open
            TransitionCircuitBreaker(providerName, CircuitState.HalfOpen);
            _logger?.LogInformation("[RESILIENCE] Circuit HALF-OPEN for provider {Provider}, testing...", providerName);
        }

        // Retry loop
        while (attempts < _config.MaxRetries)
        {
            attempts++;
            ct.ThrowIfCancellationRequested();

            try
            {
                // Execute with timeout
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(_config.CallTimeout);

                var result = await operation(timeoutCts.Token);

                // Success - reset circuit breaker
                OnSuccess(providerName);

                return new ResilienceOutcome<T>
                {
                    Success = true,
                    Result = result,
                    AttemptsUsed = attempts,
                    TotalDuration = DateTime.UtcNow - startTime
                };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // External cancellation - don't retry
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                OnFailure(providerName, ex);

                // Check if we should retry
                if (!ShouldRetry(ex, attempts))
                {
                    _logger?.LogWarning(ex,
                        "[RESILIENCE] Non-retryable failure for provider {Provider} on attempt {Attempt}",
                        providerName, attempts);
                    break;
                }

                // Calculate delay with exponential backoff + jitter
                var delay = CalculateDelay(attempts);

                _logger?.LogWarning(
                    "[RESILIENCE] Transient failure for provider {Provider} on attempt {Attempt}/{Max}, " +
                    "retrying in {Delay:F1}s. Error: {Error}",
                    providerName, attempts, _config.MaxRetries, delay.TotalSeconds, ex.Message);

                await Task.Delay(delay, ct);
            }
        }

        return new ResilienceOutcome<T>
        {
            Success = false,
            LastException = lastException,
            AttemptsUsed = attempts,
            TotalDuration = DateTime.UtcNow - startTime
        };
    }

    /// <summary>
    /// Determine if an exception is retryable.
    /// </summary>
    private static bool ShouldRetry(Exception ex, int currentAttempt)
    {
        // Retryable conditions
        return ex switch
        {
            // Timeouts are always retryable (TaskCanceledException inherits from OperationCanceledException)
            OperationCanceledException => true,

            // HTTP transient errors
            HttpRequestException hre => IsTransientHttpError(hre),

            // Rate limiting (429) - always retry with backoff
            _ when ex.Message.Contains("429") || ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) => true,

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
        // 4xx except 429 are non-retryable
        // 5xx are retryable
        return ex.Message.Contains("429") ||
               ex.Message.Contains("500") ||
               ex.Message.Contains("502") ||
               ex.Message.Contains("503") ||
               ex.Message.Contains("504");
    }

    /// <summary>
    /// Calculate retry delay with exponential backoff and optional jitter.
    /// </summary>
    private TimeSpan CalculateDelay(int attempt)
    {
        // Exponential backoff: delay = initial * multiplier^(attempt-1)
        var delay = _config.InitialRetryDelay.TotalMilliseconds *
                    Math.Pow(_config.BackoffMultiplier, attempt - 1);

        // Cap at max delay
        delay = Math.Min(delay, _config.MaxRetryDelay.TotalMilliseconds);

        // Add jitter (±25%)
        if (_config.EnableJitter)
        {
            var jitter = delay * 0.25 * (_random.NextDouble() * 2 - 1);
            delay += jitter;
        }

        return TimeSpan.FromMilliseconds(delay);
    }

    // ============================================================
    //  Circuit Breaker
    // ============================================================

    private CircuitBreakerState GetCircuitBreakerState(string providerName)
    {
        lock (_cbLock)
        {
            if (!_circuitBreakers.TryGetValue(providerName, out var state))
            {
                state = new CircuitBreakerState();
                _circuitBreakers[providerName] = state;
            }
            return state;
        }
    }

    private void TransitionCircuitBreaker(string providerName, CircuitState newState)
    {
        lock (_cbLock)
        {
            if (!_circuitBreakers.TryGetValue(providerName, out var state))
            {
                state = new CircuitBreakerState();
                _circuitBreakers[providerName] = state;
            }
            state.State = newState;
        }
    }

    private void OnSuccess(string providerName)
    {
        lock (_cbLock)
        {
            if (_circuitBreakers.TryGetValue(providerName, out var state))
            {
                if (state.State == CircuitState.HalfOpen)
                {
                    _logger?.LogInformation(
                        "[RESILIENCE] Circuit CLOSED for provider {Provider} after successful test",
                        providerName);
                }
                state.State = CircuitState.Closed;
                state.FailureCount = 0;
                state.LastSuccess = DateTime.UtcNow;
            }
        }
    }

    private void OnFailure(string providerName, Exception ex)
    {
        lock (_cbLock)
        {
            if (!_circuitBreakers.TryGetValue(providerName, out var state))
            {
                state = new CircuitBreakerState();
                _circuitBreakers[providerName] = state;
            }

            state.FailureCount++;
            state.LastFailure = DateTime.UtcNow;
            state.LastException = ex;

            // Open circuit if threshold exceeded
            if (state.FailureCount >= _config.CircuitBreakerThreshold && state.State != CircuitState.Open)
            {
                state.State = CircuitState.Open;
                state.OpenUntil = DateTime.UtcNow + _config.CircuitBreakerDuration;

                _logger?.LogError(
                    "[RESILIENCE] Circuit OPENED for provider {Provider} after {Count} failures. " +
                    "Will test again at {OpenUntil:HH:mm:ss}",
                    providerName, state.FailureCount, state.OpenUntil);
            }
        }
    }

    /// <summary>
    /// Get current health status of all providers.
    /// </summary>
    public Dictionary<string, ProviderHealthStatus> GetHealthStatus()
    {
        lock (_cbLock)
        {
            return _circuitBreakers.ToDictionary(
                kvp => kvp.Key,
                kvp => new ProviderHealthStatus
                {
                    ProviderName = kvp.Key,
                    State = kvp.Value.State,
                    FailureCount = kvp.Value.FailureCount,
                    LastSuccess = kvp.Value.LastSuccess,
                    LastFailure = kvp.Value.LastFailure,
                    OpenUntil = kvp.Value.OpenUntil
                });
        }
    }

    // ============================================================
    //  Inner Types
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

/// <summary>
/// Exception thrown when circuit breaker is open.
/// </summary>
public sealed class CircuitBreakerOpenException : Exception
{
    public string ProviderName { get; }
    public DateTime OpenUntil { get; }

    public CircuitBreakerOpenException(string providerName, DateTime openUntil)
        : base($"Circuit breaker OPEN for provider '{providerName}' until {openUntil:HH:mm:ss}")
    {
        ProviderName = providerName;
        OpenUntil = openUntil;
    }
}

/// <summary>
/// Health status for a provider.
/// </summary>
public sealed record ProviderHealthStatus
{
    public required string ProviderName { get; init; }
    public CircuitState State { get; init; }
    public int FailureCount { get; init; }
    public DateTime? LastSuccess { get; init; }
    public DateTime? LastFailure { get; init; }
    public DateTime OpenUntil { get; init; }
}

