using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// Interface for AI agent memory management.
/// </summary>
public interface IAevatarMemory
{
    /// <summary>
    /// Stores a memory item.
    /// </summary>
    /// <param name="key">The memory key.</param>
    /// <param name="value">The memory value.</param>
    /// <param name="metadata">Optional metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the storage operation.</returns>
    Task StoreAsync(string key, object value, Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a memory item.
    /// </summary>
    /// <typeparam name="T">The type to retrieve.</typeparam>
    /// <param name="key">The memory key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The memory value if found; otherwise, default(T).</returns>
    Task<T?> RetrieveAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Searches memory by query.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="maxResults">Maximum number of results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of memory search results.</returns>
    Task<List<AevatarMemorySearchResult>> SearchAsync(string query, int maxResults = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a memory item.
    /// </summary>
    /// <param name="key">The memory key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if removed; otherwise, false.</returns>
    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all memory.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the clear operation.</returns>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets memory statistics.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Memory statistics.</returns>
    Task<AevatarMemoryStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a memory search result.
/// </summary>
public class AevatarMemorySearchResult
{
    /// <summary>
    /// Gets or sets the memory key.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the memory value.
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// Gets or sets the relevance score.
    /// </summary>
    public double RelevanceScore { get; set; }

    /// <summary>
    /// Gets or sets metadata.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// Gets or sets when the memory was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets when the memory was last accessed.
    /// </summary>
    public DateTime? LastAccessedAt { get; set; }
}

