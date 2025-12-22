using Aevatar.Agents.Abstractions.Context;
using Orleans.Runtime;

namespace Aevatar.Agents.Runtime.Orleans.Context;

/// <summary>
/// Bridge implementation that reads/writes Orleans RequestContext.
/// Provides IAgentContext interface over Orleans' built-in request context.
/// </summary>
public class OrleansAgentContextBridge : IAgentContext
{
    // Track keys that have been set through this bridge for GetAll() enumeration
    private readonly HashSet<string> _trackedKeys = new();
    private readonly object _lock = new();

    /// <inheritdoc />
    public T? Get<T>(AgentContextKey<T> key)
    {
        var value = RequestContext.Get(key.Name);
        return value is T typedValue ? typedValue : key.DefaultValue;
    }

    /// <inheritdoc />
    public object? Get(string key)
    {
        return RequestContext.Get(key);
    }

    /// <inheritdoc />
    public void Set<T>(AgentContextKey<T> key, T value)
    {
        if (value != null)
        {
            RequestContext.Set(key.Name, value);
            TrackKey(key.Name);
        }
        else
        {
            Remove(key.Name);
        }
    }

    /// <inheritdoc />
    public void Set(string key, object? value)
    {
        if (value != null)
        {
            RequestContext.Set(key, value);
            TrackKey(key);
        }
        else
        {
            Remove(key);
        }
    }

    /// <inheritdoc />
    public void Remove(string key)
    {
        // Orleans RequestContext doesn't have a remove method,
        // use an empty string marker to indicate removal
        RequestContext.Set(key, string.Empty);
        UntrackKey(key);
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_lock)
        {
            foreach (var key in _trackedKeys)
            {
                RequestContext.Set(key, string.Empty);
            }
            _trackedKeys.Clear();
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> GetAll()
    {
        // Orleans RequestContext doesn't expose enumeration,
        // so we return values for tracked keys only
        var result = new Dictionary<string, object?>();
        
        lock (_lock)
        {
            foreach (var key in _trackedKeys)
            {
                var value = RequestContext.Get(key);
                if (value != null)
                {
                    result[key] = value;
                }
            }
        }

        // Also check well-known keys from AgentContextKeys.AllowedPropagationKeys
        foreach (var key in AgentContextKeys.AllowedPropagationKeys)
        {
            if (!result.ContainsKey(key))
            {
                var value = RequestContext.Get(key);
                if (value != null)
                {
                    result[key] = value;
                    TrackKey(key);
                }
            }
        }

        return result;
    }

    /// <inheritdoc />
    public void Import(IReadOnlyDictionary<string, object?> entries)
    {
        foreach (var (key, value) in entries)
        {
            Set(key, value);
        }
    }

    private void TrackKey(string key)
    {
        lock (_lock)
        {
            _trackedKeys.Add(key);
        }
    }

    private void UntrackKey(string key)
    {
        lock (_lock)
        {
            _trackedKeys.Remove(key);
        }
    }
}

