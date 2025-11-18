using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions.Persistence;

namespace Aevatar.Agents.Core.Persistence;

/// <summary>
/// In-memory configuration store implementation
/// Thread-safe using ConcurrentDictionary
/// </summary>
/// <typeparam name="TConfig">Configuration type</typeparam>
public class InMemoryConfigStore<TConfig> : IConfigStore<TConfig>
    where TConfig : class, new()
{
    private readonly ConcurrentDictionary<Guid, TConfig> _configs = new();

    /// <summary>
    /// Load configuration from memory
    /// </summary>
    public Task<TConfig?> LoadAsync(Guid agentId, CancellationToken ct = default)
    {
        _configs.TryGetValue(agentId, out var config);
        return Task.FromResult(config);
    }

    /// <summary>
    /// Save configuration to memory
    /// </summary>
    public Task SaveAsync(Guid agentId, TConfig config, CancellationToken ct = default)
    {
        _configs[agentId] = config;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Delete configuration from memory
    /// </summary>
    public Task DeleteAsync(Guid agentId, CancellationToken ct = default)
    {
        _configs.TryRemove(agentId, out _);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Check if configuration exists
    /// </summary>
    public Task<bool> ExistsAsync(Guid agentId, CancellationToken ct = default)
    {
        return Task.FromResult(_configs.ContainsKey(agentId));
    }

    /// <summary>
    /// Get all stored configurations (for testing/debugging)
    /// </summary>
    public IReadOnlyDictionary<Guid, TConfig> GetAllConfigs()
    {
        return _configs;
    }

    /// <summary>
    /// Clear all configurations (for testing)
    /// </summary>
    public void Clear()
    {
        _configs.Clear();
    }
}



