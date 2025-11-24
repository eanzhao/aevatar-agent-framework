using System;
using Aevatar.Agents.Abstractions;

namespace Aevatar.Agents.Core.Subscription;

/// <summary>
/// Fixed interval retry policy
/// </summary>
public class FixedIntervalRetryPolicy : IRetryPolicy
{
    private readonly TimeSpan _interval;

    public FixedIntervalRetryPolicy(int maxRetries = 3, TimeSpan? interval = null)
    {
        MaxRetries = maxRetries;
        _interval = interval ?? TimeSpan.FromSeconds(1);
    }

    public int MaxRetries { get; }

    public TimeSpan GetDelay(int attemptNumber)
    {
        return _interval;
    }

    public bool ShouldRetry(Exception exception, int attemptNumber)
    {
        return attemptNumber <= MaxRetries && IsTransientException(exception);
    }

    protected virtual bool IsTransientException(Exception exception)
    {
        // Can determine if it's a transient error based on specific exception type
        return exception is not ArgumentException and not InvalidOperationException;
    }
}

/// <summary>
/// Exponential backoff retry policy
/// </summary>
public class ExponentialBackoffRetryPolicy : IRetryPolicy
{
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _maxDelay;
    private readonly double _backoffMultiplier;
    private readonly Random _jitter = new();

    public ExponentialBackoffRetryPolicy(
        int maxRetries = 5,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null,
        double backoffMultiplier = 2.0,
        bool useJitter = true)
    {
        MaxRetries = maxRetries;
        _initialDelay = initialDelay ?? TimeSpan.FromMilliseconds(100);
        _maxDelay = maxDelay ?? TimeSpan.FromSeconds(30);
        _backoffMultiplier = backoffMultiplier;
        UseJitter = useJitter;
    }

    public int MaxRetries { get; }
    public bool UseJitter { get; }

    public TimeSpan GetDelay(int attemptNumber)
    {
        // Calculate exponential backoff delay
        var exponentialDelay = _initialDelay.TotalMilliseconds * Math.Pow(_backoffMultiplier, attemptNumber - 1);
        
        // Limit maximum delay
        var delayMs = Math.Min(exponentialDelay, _maxDelay.TotalMilliseconds);
        
        // Add jitter to avoid retry storms
        if (UseJitter)
        {
            delayMs = delayMs * (0.5 + _jitter.NextDouble() * 0.5);
        }
        
        return TimeSpan.FromMilliseconds(delayMs);
    }

    public bool ShouldRetry(Exception exception, int attemptNumber)
    {
        return attemptNumber <= MaxRetries && IsTransientException(exception);
    }

    protected virtual bool IsTransientException(Exception exception)
    {
        // Determine if it's a transient error
        return exception switch
        {
            TimeoutException => true,
            OperationCanceledException => false,
            ArgumentException => false,
            InvalidOperationException => false,
            _ => true // Default assumption is transient error
        };
    }
}

/// <summary>
/// Linear backoff retry policy
/// </summary>
public class LinearBackoffRetryPolicy : IRetryPolicy
{
    private readonly TimeSpan _delayIncrement;
    private readonly TimeSpan _maxDelay;

    public LinearBackoffRetryPolicy(
        int maxRetries = 4,
        TimeSpan? delayIncrement = null,
        TimeSpan? maxDelay = null)
    {
        MaxRetries = maxRetries;
        _delayIncrement = delayIncrement ?? TimeSpan.FromSeconds(1);
        _maxDelay = maxDelay ?? TimeSpan.FromSeconds(10);
    }

    public int MaxRetries { get; }

    public TimeSpan GetDelay(int attemptNumber)
    {
        var delay = TimeSpan.FromMilliseconds(_delayIncrement.TotalMilliseconds * attemptNumber);
        return delay > _maxDelay ? _maxDelay : delay;
    }

    public bool ShouldRetry(Exception exception, int attemptNumber)
    {
        return attemptNumber <= MaxRetries && IsTransientException(exception);
    }

    protected virtual bool IsTransientException(Exception exception)
    {
        return exception is not ArgumentException and not InvalidOperationException;
    }
}

/// <summary>
/// No retry policy
/// </summary>
public class NoRetryPolicy : IRetryPolicy
{
    public int MaxRetries => 0;

    public TimeSpan GetDelay(int attemptNumber)
    {
        return TimeSpan.Zero;
    }

    public bool ShouldRetry(Exception exception, int attemptNumber)
    {
        return false;
    }
}

/// <summary>
/// Retry policy factory
/// </summary>
public static class RetryPolicyFactory
{
    /// <summary>
    /// Create default retry policy (exponential backoff)
    /// </summary>
    public static IRetryPolicy CreateDefault()
    {
        return new ExponentialBackoffRetryPolicy();
    }

    /// <summary>
    /// Create fixed interval retry policy
    /// </summary>
    public static IRetryPolicy CreateFixedInterval(int maxRetries = 3, TimeSpan? interval = null)
    {
        return new FixedIntervalRetryPolicy(maxRetries, interval);
    }

    /// <summary>
    /// Create exponential backoff retry policy
    /// </summary>
    public static IRetryPolicy CreateExponentialBackoff(
        int maxRetries = 5,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null,
        bool useJitter = true)
    {
        return new ExponentialBackoffRetryPolicy(maxRetries, initialDelay, maxDelay, useJitter: useJitter);
    }

    /// <summary>
    /// Create linear backoff retry policy
    /// </summary>
    public static IRetryPolicy CreateLinearBackoff(
        int maxRetries = 4,
        TimeSpan? delayIncrement = null,
        TimeSpan? maxDelay = null)
    {
        return new LinearBackoffRetryPolicy(maxRetries, delayIncrement, maxDelay);
    }

    /// <summary>
    /// Create no retry policy
    /// </summary>
    public static IRetryPolicy CreateNoRetry()
    {
        return new NoRetryPolicy();
    }
}

