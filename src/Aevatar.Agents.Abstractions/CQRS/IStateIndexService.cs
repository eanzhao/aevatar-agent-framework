namespace Aevatar.Agents.Abstractions.CQRS;

/// <summary>
/// State indexing service interface for CQRS read model.
/// Responsible for indexing, querying, and managing state documents in Elasticsearch.
/// </summary>
public interface IStateIndexService
{
    // ============ Write Operations ============

    /// <summary>
    /// Ensures the index exists for the given agent type.
    /// Creates the index with dynamic mapping if it doesn't exist.
    /// </summary>
    /// <param name="agentType">Agent type name (used for index naming)</param>
    /// <param name="stateType">Optional state type for generating precise mapping</param>
    /// <param name="ct">Cancellation token</param>
    Task EnsureIndexExistsAsync(string agentType, Type? stateType = null, CancellationToken ct = default);

    /// <summary>
    /// Indexes a single state document with version control.
    /// Uses ScriptedUpsert for optimistic concurrency.
    /// </summary>
    /// <param name="document">The state document to index</param>
    /// <param name="ct">Cancellation token</param>
    Task IndexStateAsync(StateIndexDocument document, CancellationToken ct = default);

    /// <summary>
    /// Indexes multiple state documents in batch.
    /// More efficient for high-throughput scenarios.
    /// </summary>
    /// <param name="documents">Collection of state documents to index</param>
    /// <param name="ct">Cancellation token</param>
    Task IndexStateBatchAsync(IEnumerable<StateIndexDocument> documents, CancellationToken ct = default);

    /// <summary>
    /// Deletes a state document from the index.
    /// </summary>
    /// <param name="agentType">Agent type (determines index name)</param>
    /// <param name="agentId">Agent ID (document ID)</param>
    /// <param name="ct">Cancellation token</param>
    Task DeleteStateAsync(string agentType, string agentId, CancellationToken ct = default);

    // ============ Query Operations ============

    /// <summary>
    /// Gets a single state document by agent ID.
    /// </summary>
    /// <param name="agentType">Agent type (determines index name)</param>
    /// <param name="agentId">Agent ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>State query result or null if not found</returns>
    Task<StateQueryResult?> GetByIdAsync(string agentType, string agentId, CancellationToken ct = default);

    /// <summary>
    /// Queries states using Lucene query syntax.
    /// Supports pagination, sorting, and filtering.
    /// </summary>
    /// <param name="query">Query parameters</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Paged query results</returns>
    Task<PagedStateQueryResult> QueryAsync(StateQuery query, CancellationToken ct = default);

    /// <summary>
    /// Counts documents matching the query.
    /// </summary>
    /// <param name="agentType">Agent type (determines index name)</param>
    /// <param name="queryString">Optional Lucene query string</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Document count</returns>
    Task<long> CountAsync(string agentType, string? queryString = null, CancellationToken ct = default);
}

/// <summary>
/// State document for indexing to Elasticsearch.
/// </summary>
public class StateIndexDocument
{
    /// <summary>
    /// Agent ID (used as ES document _id)
    /// </summary>
    public string AgentId { get; set; } = string.Empty;

    /// <summary>
    /// Agent type (used for determining index name)
    /// </summary>
    public string AgentType { get; set; } = string.Empty;

    /// <summary>
    /// State data as key-value pairs.
    /// Basic types stored directly, complex types serialized as JSON strings.
    /// </summary>
    public Dictionary<string, object> Data { get; set; } = new();

    /// <summary>
    /// Version number for optimistic concurrency control
    /// </summary>
    public long Version { get; set; }

    /// <summary>
    /// Timestamp when the document was indexed
    /// </summary>
    public DateTime IndexedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Query parameters for Lucene-style queries.
/// </summary>
public class StateQuery
{
    /// <summary>
    /// Agent type (determines which index to query)
    /// </summary>
    public string AgentType { get; set; } = string.Empty;

    /// <summary>
    /// Lucene query string (e.g., "username:alice* AND isActive:true")
    /// </summary>
    public string? QueryString { get; set; }

    /// <summary>
    /// Page index (0-based)
    /// </summary>
    public int PageIndex { get; set; } = 0;

    /// <summary>
    /// Page size
    /// </summary>
    public int PageSize { get; set; } = 20;

    /// <summary>
    /// Sort fields in format "fieldName:asc" or "fieldName:desc"
    /// </summary>
    public List<string> SortFields { get; set; } = new();
}

/// <summary>
/// Single state query result.
/// </summary>
public class StateQueryResult
{
    public string AgentId { get; set; } = string.Empty;
    public string AgentType { get; set; } = string.Empty;
    public Dictionary<string, object?> Data { get; set; } = new();
    public long Version { get; set; }
    public DateTime? IndexedAt { get; set; }
}

/// <summary>
/// Paged state query result.
/// </summary>
public class PagedStateQueryResult
{
    public long TotalCount { get; set; }
    public List<StateQueryResult> Items { get; set; } = new();
    public int PageIndex { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
}

