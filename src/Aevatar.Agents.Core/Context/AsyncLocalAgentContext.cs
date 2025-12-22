using Aevatar.Agents.Abstractions.Context;

namespace Aevatar.Agents.Core.Context;

/// <summary>
/// Default IAgentContext implementation using dictionary storage.
/// Thread-safe implementation for concurrent access.
/// </summary>
public class AsyncLocalAgentContext : IAgentContext
{
    private readonly Dictionary<string, object?> _data = new();
    private readonly object _lock = new();

    /// <inheritdoc />
    public T? Get<T>(AgentContextKey<T> key)
    {
        lock (_lock)
        {
            return _data.TryGetValue(key.Name, out var value) && value is T typedValue
                ? typedValue
                : key.DefaultValue;
        }
    }

    /// <inheritdoc />
    public object? Get(string key)
    {
        lock (_lock)
        {
            return _data.TryGetValue(key, out var value) ? value : null;
        }
    }

    /// <inheritdoc />
    public void Set<T>(AgentContextKey<T> key, T value)
    {
        lock (_lock)
        {
            _data[key.Name] = value;
        }
    }

    /// <inheritdoc />
    public void Set(string key, object? value)
    {
        lock (_lock)
        {
            _data[key] = value;
        }
    }

    /// <inheritdoc />
    public void Remove(string key)
    {
        lock (_lock)
        {
            _data.Remove(key);
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_lock)
        {
            _data.Clear();
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> GetAll()
    {
        lock (_lock)
        {
            return new Dictionary<string, object?>(_data);
        }
    }

    /// <inheritdoc />
    public void Import(IReadOnlyDictionary<string, object?> entries)
    {
        lock (_lock)
        {
            foreach (var (key, value) in entries)
            {
                _data[key] = value;
            }
        }
    }
}

