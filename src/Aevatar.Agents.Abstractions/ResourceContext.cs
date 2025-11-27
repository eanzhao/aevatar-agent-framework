namespace Aevatar.Agents.Abstractions;

// ============================================================
//  Resource Context - Type-Safe Resource Management
//  Provides typed access to agent resources
// ============================================================

/// <summary>
/// Resource context providing type-safe access to agent resources
/// </summary>
public class ResourceContext
{
    private readonly Dictionary<string, object> _resources = new();

    /// <summary>
    /// Resource metadata
    /// </summary>
    public Dictionary<string, ResourceMetadata> Metadata { get; } = new();

    /// <summary>
    /// Get resource by key with type safety
    /// </summary>
    /// <typeparam name="T">Expected resource type</typeparam>
    /// <param name="key">Resource key</param>
    /// <returns>Resource or null if not found or type mismatch</returns>
    public T? Get<T>(string key) where T : class
    {
        return _resources.TryGetValue(key, out var value) ? value as T : null;
    }

    /// <summary>
    /// Get required resource - throws if not found or type mismatch
    /// </summary>
    /// <typeparam name="T">Expected resource type</typeparam>
    /// <param name="key">Resource key</param>
    /// <returns>Resource</returns>
    /// <exception cref="KeyNotFoundException">If resource not found</exception>
    /// <exception cref="InvalidCastException">If type mismatch</exception>
    public T GetRequired<T>(string key) where T : class
    {
        if (!_resources.TryGetValue(key, out var value))
            throw new KeyNotFoundException($"Resource '{key}' not found");

        return value as T
               ?? throw new InvalidCastException($"Resource '{key}' is not of type {typeof(T).Name}");
    }

    /// <summary>
    /// Set resource with type safety
    /// </summary>
    /// <typeparam name="T">Resource type</typeparam>
    /// <param name="key">Resource key</param>
    /// <param name="value">Resource value</param>
    /// <param name="description">Optional description</param>
    public void Set<T>(string key, T value, string? description = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        _resources[key] = value;
        Metadata[key] = new ResourceMetadata
        {
            Key = key,
            Type = typeof(T).Name,
            Description = description ?? string.Empty,
            AddedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Check if resource exists
    /// </summary>
    /// <param name="key">Resource key</param>
    /// <returns>True if exists</returns>
    public bool Contains(string key) => _resources.ContainsKey(key);

    /// <summary>
    /// Try to get resource
    /// </summary>
    /// <typeparam name="T">Expected resource type</typeparam>
    /// <param name="key">Resource key</param>
    /// <param name="value">Output value</param>
    /// <returns>True if found and type matches</returns>
    public bool TryGet<T>(string key, out T? value) where T : class
    {
        if (_resources.TryGetValue(key, out var obj) && obj is T typed)
        {
            value = typed;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Remove resource
    /// </summary>
    /// <param name="key">Resource key</param>
    /// <returns>True if removed</returns>
    public bool Remove(string key)
    {
        Metadata.Remove(key);
        return _resources.Remove(key);
    }

    /// <summary>
    /// Get all resource keys
    /// </summary>
    public IEnumerable<string> Keys => _resources.Keys;

    /// <summary>
    /// Get resource count
    /// </summary>
    public int Count => _resources.Count;

    /// <summary>
    /// Clear all resources
    /// </summary>
    public void Clear()
    {
        _resources.Clear();
        Metadata.Clear();
    }

    #region Legacy API (backward compatible)

    /// <summary>
    /// Available resources dictionary (legacy API)
    /// </summary>
    [Obsolete("Use Get<T>/Set<T> methods for type safety. This property will be removed in future versions.")]
    public Dictionary<string, object> AvailableResources
    {
        get => new(_resources);
        set
        {
            _resources.Clear();
            Metadata.Clear();
            foreach (var kv in value)
            {
                _resources[kv.Key] = kv.Value;
                Metadata[kv.Key] = new ResourceMetadata
                {
                    Key = kv.Key,
                    Type = kv.Value?.GetType().Name ?? "null",
                    Description = string.Empty,
                    AddedAt = DateTime.UtcNow
                };
            }
        }
    }

    /// <summary>
    /// Add resource (legacy API)
    /// </summary>
    [Obsolete("Use Set<T> method for type safety")]
    public void AddResource(string key, object resource, string? description = null)
    {
        _resources[key] = resource;
        Metadata[key] = new ResourceMetadata
        {
            Key = key,
            Type = resource.GetType().Name,
            Description = description ?? string.Empty,
            AddedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Get resource (legacy API)
    /// </summary>
    [Obsolete("Use Get<T> method for type safety")]
    public T? GetResource<T>(string key) where T : class => Get<T>(key);

    /// <summary>
    /// Remove resource (legacy API)
    /// </summary>
    [Obsolete("Use Remove method instead")]
    public bool RemoveResource(string key) => Remove(key);

    #endregion
}

/// <summary>
/// Resource metadata
/// </summary>
public class ResourceMetadata
{
    public string Key { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; }
}
