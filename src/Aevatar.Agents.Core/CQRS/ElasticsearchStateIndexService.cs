using System.Collections.Concurrent;
using System.Text.Json;
using Aevatar.Agents.Abstractions.CQRS;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Bulk;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Clients.Elasticsearch.Mapping;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.Extensions.Logging;
using ProtobufTimestamp = Google.Protobuf.WellKnownTypes.Timestamp;
using EsField = Elastic.Clients.Elasticsearch.Field;

namespace Aevatar.Agents.Core.CQRS;

/// <summary>
/// Elasticsearch implementation of IStateIndexService.
/// Handles dynamic mapping, ScriptedUpsert for optimistic concurrency,
/// and Lucene-style querying.
/// </summary>
public class ElasticsearchStateIndexService : IStateIndexService
{
    private readonly ElasticsearchClient _client;
    private readonly ILogger<ElasticsearchStateIndexService> _logger;
    private readonly ConcurrentDictionary<string, bool> _indexCache = new();
    private readonly ElasticsearchOptions _options;

    public ElasticsearchStateIndexService(
        ElasticsearchClient client,
        ILogger<ElasticsearchStateIndexService> logger,
        ElasticsearchOptions? options = null)
    {
        _client = client;
        _logger = logger;
        _options = options ?? new ElasticsearchOptions();
    }

    #region Index Name Generation

    private string GetIndexName(string agentType)
    {
        var typeName = agentType.ToLowerInvariant()
            .Replace(".", "-")
            .Replace("+", "-");
        return $"{_options.IndexPrefix}-{typeName}";
    }

    #endregion

    #region Write Operations

    public async Task EnsureIndexExistsAsync(string agentType, System.Type? stateType = null, CancellationToken ct = default)
    {
        var indexName = GetIndexName(agentType);

        if (_indexCache.TryGetValue(indexName, out _))
            return;

        var existsResponse = await _client.Indices.ExistsAsync(indexName, ct);
        if (existsResponse.Exists)
        {
            _indexCache[indexName] = true;
            return;
        }

        // Create index with dynamic mapping
        var properties = GenerateDynamicMapping(stateType);
        var createResponse = await _client.Indices.CreateAsync(indexName, c =>
        {
            c.Mappings(m => m.Properties(properties));
        }, ct);

        if (!createResponse.IsValidResponse)
        {
            _logger.LogError(
                "Failed to create index {IndexName}: {Error}",
                indexName,
                createResponse.ElasticsearchServerError?.Error?.Reason);
            return;
        }

        _logger.LogInformation("Created index {IndexName}", indexName);
        _indexCache[indexName] = true;
    }

    public async Task IndexStateAsync(StateIndexDocument document, CancellationToken ct = default)
    {
        var indexName = GetIndexName(document.AgentType);

        // Prepare document with system fields
        var esDocument = PrepareDocument(document);

        // Use Update with ScriptedUpsert for optimistic concurrency
        var response = await _client.UpdateAsync<Dictionary<string, object>, Dictionary<string, object>>(
            indexName,
            document.AgentId,
            u => u
                .Script(s => s
                    .Source(VersionCheckScript)
                    .Params(p => p
                        .Add("version", document.Version)
                        .Add("doc", esDocument)
                    )
                )
                .ScriptedUpsert(true)
                .Upsert(esDocument),
            ct);

        if (!response.IsValidResponse)
        {
            _logger.LogError(
                "Failed to index state for {AgentId}: {Error}",
                document.AgentId,
                response.ElasticsearchServerError?.Error?.Reason);
        }
    }

    public async Task IndexStateBatchAsync(IEnumerable<StateIndexDocument> documents, CancellationToken ct = default)
    {
        var bulkOperations = new BulkOperationsCollection();

        foreach (var document in documents)
        {
            var indexName = GetIndexName(document.AgentType);
            var esDocument = PrepareDocument(document);

            var operation = new BulkUpdateOperation<Dictionary<string, object>, object>(document.AgentId)
            {
                Index = indexName,
                Script = new Script
                {
                    Source = VersionCheckScript,
                    Params = new Dictionary<string, object>
                    {
                        ["version"] = document.Version,
                        ["doc"] = esDocument
                    }
                },
                ScriptedUpsert = true,
                Upsert = esDocument
            };

            bulkOperations.Add(operation);
        }

        var bulkRequest = new BulkRequest
        {
            Operations = bulkOperations,
            Refresh = Refresh.WaitFor
        };

        var response = await _client.BulkAsync(bulkRequest, ct);

        if (response.Errors)
        {
            var errors = response.Items
                .Where(i => i.Error != null)
                .Select(i => new { i.Id, i.Error?.Reason });

            _logger.LogError(
                "Bulk index had {ErrorCount} errors: {Errors}",
                errors.Count(),
                System.Text.Json.JsonSerializer.Serialize(errors));
        }
    }

    public async Task DeleteStateAsync(string agentType, string agentId, CancellationToken ct = default)
    {
        var indexName = GetIndexName(agentType);

        var response = await _client.DeleteAsync(indexName, Id.From(agentId), ct);

        if (!response.IsValidResponse && response.Result != Result.NotFound)
        {
            _logger.LogError(
                "Failed to delete state for {AgentId}: {Error}",
                agentId,
                response.ElasticsearchServerError?.Error?.Reason);
        }
    }

    #endregion

    #region Query Operations

    public async Task<StateQueryResult?> GetByIdAsync(string agentType, string agentId, CancellationToken ct = default)
    {
        var indexName = GetIndexName(agentType);

        var response = await _client.GetAsync<Dictionary<string, object?>>(indexName, agentId, ct);

        if (!response.IsValidResponse || !response.Found)
            return null;

        return MapToQueryResult(response.Source!, agentId, agentType);
    }

    public async Task<PagedStateQueryResult> QueryAsync(StateQuery query, CancellationToken ct = default)
    {
        var indexName = GetIndexName(query.AgentType);
        var from = query.PageIndex * query.PageSize;

        // Build sort options
        var sortOptions = BuildSortOptions(query.SortFields);

        var searchRequest = new SearchRequest<Dictionary<string, object?>>(indexName)
        {
            From = from,
            Size = query.PageSize,
            Sort = sortOptions
        };

        // Add query if specified
        if (!string.IsNullOrEmpty(query.QueryString))
        {
            searchRequest.Query = new QueryStringQuery
            {
                Query = query.QueryString,
                AllowLeadingWildcard = false
            };
        }

        var response = await _client.SearchAsync<Dictionary<string, object?>>(searchRequest, ct);

        if (!response.IsValidResponse)
        {
            var error = response.ElasticsearchServerError?.Error?.Reason ?? "Unknown error";
            _logger.LogError("Query failed: {Error}", error);
            throw new InvalidOperationException($"ES Query Failed: {error}");
        }

        var items = response.Hits
            .Where(h => h.Source != null)
            .Select(h => MapToQueryResult(h.Source!, h.Id!, query.AgentType))
            .ToList();

        return new PagedStateQueryResult
        {
            TotalCount = response.Total,
            Items = items,
            PageIndex = query.PageIndex,
            PageSize = query.PageSize
        };
    }

    public async Task<long> CountAsync(string agentType, string? queryString = null, CancellationToken ct = default)
    {
        var indexName = GetIndexName(agentType);

        var countRequest = new CountRequest(indexName);

        if (!string.IsNullOrEmpty(queryString))
        {
            countRequest.Query = new QueryStringQuery
            {
                Query = queryString,
                AllowLeadingWildcard = false
            };
        }

        var response = await _client.CountAsync(countRequest, ct);

        if (!response.IsValidResponse)
        {
            var error = response.ElasticsearchServerError?.Error?.Reason ?? "Unknown error";
            _logger.LogError("Count failed: {Error}", error);
            throw new InvalidOperationException($"ES Count Failed: {error}");
        }

        return response.Count;
    }

    #endregion

    #region Helper Methods

    private const string VersionCheckScript = @"
        if (ctx.op == 'create' || 
            ctx._source.version == null || 
            params.version > ctx._source.version) 
        { 
            ctx._source = params.doc; 
        } 
        else 
        { 
            ctx.op = 'noop'; 
        }";

    private Dictionary<string, object> PrepareDocument(StateIndexDocument document)
    {
        var esDocument = new Dictionary<string, object>(document.Data)
        {
            ["agentId"] = document.AgentId,
            ["agentType"] = document.AgentType,
            ["version"] = document.Version,
            ["indexedAt"] = document.IndexedAt
        };

        return esDocument;
    }

    private Properties GenerateDynamicMapping(System.Type? stateType)
    {
        var props = new Properties();

        // System fields
        props["agentId"] = new KeywordProperty();
        props["agentType"] = new KeywordProperty();
        props["version"] = new LongNumberProperty();
        props["indexedAt"] = new DateProperty();

        // If state type is provided, generate mapping for its properties
        if (stateType != null)
        {
            GeneratePropertiesMapping(props, stateType);
        }

        return props;
    }

    private void GeneratePropertiesMapping(Properties props, System.Type stateType)
    {
        foreach (var property in stateType.GetProperties())
        {
            var propertyName = ToCamelCase(property.Name);
            var propType = property.PropertyType;

            // Handle nullable types
            var underlyingType = Nullable.GetUnderlyingType(propType) ?? propType;

            if (underlyingType == typeof(string))
            {
                props[propertyName] = new TextProperty();
            }
            else if (underlyingType == typeof(int) || underlyingType == typeof(long) ||
                     underlyingType == typeof(short) || underlyingType == typeof(byte))
            {
                props[propertyName] = new LongNumberProperty();
            }
            else if (underlyingType == typeof(float))
            {
                props[propertyName] = new FloatNumberProperty();
            }
            else if (underlyingType == typeof(double) || underlyingType == typeof(decimal))
            {
                props[propertyName] = new DoubleNumberProperty();
            }
            else if (underlyingType == typeof(bool))
            {
                props[propertyName] = new BooleanProperty();
            }
            else if (underlyingType == typeof(DateTime) || underlyingType == typeof(DateTimeOffset))
            {
                props[propertyName] = new DateProperty();
            }
            else if (underlyingType == typeof(ProtobufTimestamp))
            {
                props[propertyName] = new DateProperty();
            }
            else if (underlyingType == typeof(Guid))
            {
                props[propertyName] = new KeywordProperty();
            }
            else
            {
                // Complex types - stored as text (JSON serialized)
                props[propertyName] = new TextProperty();
            }
        }
    }

    private List<SortOptions> BuildSortOptions(List<string> sortFields)
    {
        var sortOptions = new List<SortOptions>();

        foreach (var sortField in sortFields)
        {
            var parts = sortField.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                _logger.LogWarning("Invalid sort field: {SortField}", sortField);
                continue;
            }

            var fieldName = parts[0].Trim();
            var sortOrder = parts[1].Trim().ToLowerInvariant();

            if (sortOrder != "asc" && sortOrder != "desc")
            {
                _logger.LogWarning("Invalid sort order for field: {Field}", fieldName);
                continue;
            }

            var order = sortOrder == "desc" ? SortOrder.Desc : SortOrder.Asc;
            var field = new EsField(fieldName);
            var fieldSort = new FieldSort { Order = order };
            sortOptions.Add(SortOptions.Field(field, fieldSort));
        }

        return sortOptions;
    }

    private StateQueryResult MapToQueryResult(Dictionary<string, object?> source, string agentId, string agentType)
    {
        var data = ConvertJsonElementToDictionary(source);

        // Extract version
        long version = 0;
        if (data.TryGetValue("version", out var versionObj) && versionObj != null)
        {
            version = Convert.ToInt64(versionObj);
        }

        // Extract indexedAt
        DateTime? indexedAt = null;
        if (data.TryGetValue("indexedAt", out var indexedAtObj) && indexedAtObj != null)
        {
            if (indexedAtObj is DateTime dt)
                indexedAt = dt;
            else if (DateTime.TryParse(indexedAtObj.ToString(), out var parsed))
                indexedAt = parsed;
        }

        return new StateQueryResult
        {
            AgentId = agentId,
            AgentType = agentType,
            Data = data,
            Version = version,
            IndexedAt = indexedAt
        };
    }

    private static Dictionary<string, object?> ConvertJsonElementToDictionary(Dictionary<string, object?> source)
    {
        var result = new Dictionary<string, object?>();

        foreach (var kvp in source)
        {
            if (kvp.Value is JsonElement element)
            {
                result[kvp.Key] = ConvertJsonElement(element);
            }
            else
            {
                result[kvp.Key] = kvp.Value;
            }
        }

        return result;
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                prop => prop.Name,
                prop => ConvertJsonElement(prop.Value)
            ),
            _ => null
        };
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    #endregion
}

/// <summary>
/// Configuration options for Elasticsearch state indexing.
/// </summary>
public class ElasticsearchOptions
{
    /// <summary>
    /// Elasticsearch URL
    /// </summary>
    public string Url { get; set; } = "http://localhost:9200";

    /// <summary>
    /// Index name prefix
    /// </summary>
    public string IndexPrefix { get; set; } = "aevatar-state";

    /// <summary>
    /// Refresh policy for write operations
    /// </summary>
    public Refresh RefreshPolicy { get; set; } = Refresh.WaitFor;
}

