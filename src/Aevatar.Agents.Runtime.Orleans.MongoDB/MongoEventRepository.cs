using Aevatar.Agents.Orleans.MongoDB;
using Aevatar.Agents.Runtime.Orleans.EventSourcing;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Aevatar.Agents.Runtime.Orleans.MongoDB;

/// <summary>
/// MongoDB document for storing a SINGLE event
/// Each event is stored as a separate document for better scalability and query performance
/// </summary>
public class EventDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = string.Empty; // Format: "{agentId}_{version}"
    
    [BsonElement("agentId")]
    [BsonRepresentation(BsonType.String)]
    public Guid AgentId { get; set; }
    
    [BsonElement("version")]
    public long Version { get; set; }
    
    [BsonElement("eventData")]
    public byte[] EventData { get; set; } = Array.Empty<byte>();
    
    [BsonElement("timestamp")]
    public DateTime Timestamp { get; set; }
    
    [BsonElement("eventType")]
    public string EventType { get; set; } = string.Empty;
}

/// <summary>
/// MongoDB implementation of IEventRepository
/// Each event is stored as a separate document for:
/// - No document size limit (16MB per doc, but unlimited docs)
/// - Efficient version-based queries
/// - Easy cleanup of old events
/// - Better indexing and performance
/// 
/// IMPORTANT: Each agent type should have its own collection for:
/// - Better query performance (smaller index)
/// - Isolated scaling and optimization
/// - Business-level separation
/// </summary>
public class MongoEventRepository : IEventRepository
{
    private readonly IMongoCollection<EventDocument> _collection;
    private readonly ILogger<MongoEventRepository> _logger;
    private readonly string _collectionName;
    private readonly MongoEventRepositoryOptions _options;
    private static readonly SemaphoreSlim _globalIndexLock = new(1, 1);
    private static readonly HashSet<string> _indexedCollections = new();

    /// <summary>
    /// Creates a new MongoEventRepository with configuration options
    /// </summary>
    public MongoEventRepository(
        IMongoClient mongoClient,
        MongoEventRepositoryOptions options,
        ILogger<MongoEventRepository> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        var database = mongoClient.GetDatabase(options.DatabaseName);
        _collection = database.GetCollection<EventDocument>(options.CollectionName);
        _collectionName = options.CollectionName;
        
        if (_options.EnableDetailedLogging)
        {
            _logger.LogInformation(
                "MongoEventRepository initialized: Database={Database}, Collection={Collection}, " +
                "MaxPoolSize={MaxPoolSize}, MinPoolSize={MinPoolSize}",
                options.DatabaseName, options.CollectionName,
                options.MaxConnectionPoolSize, options.MinConnectionPoolSize);
        }
        
        // Eagerly ensure indexes on construction (async fire-and-forget)
        _ = EnsureIndexesAsync();
    }
    
    /// <summary>
    /// Creates a new MongoEventRepository with simple parameters (backward compatibility)
    /// </summary>
    public MongoEventRepository(
        IMongoClient mongoClient,
        string databaseName,
        string collectionName,
        ILogger<MongoEventRepository> logger)
        : this(mongoClient, 
              new MongoEventRepositoryOptions 
              { 
                  DatabaseName = databaseName, 
                  CollectionName = collectionName 
              }, 
              logger)
    {
    }

    /// <summary>
    /// Ensure indexes are created eagerly on repository construction
    /// Uses global lock to ensure each collection is indexed only once across all instances
    /// </summary>
    private async Task EnsureIndexesAsync(CancellationToken ct = default)
    {
        // Check if this collection has already been indexed
        if (_indexedCollections.Contains(_collectionName))
            return;

        await _globalIndexLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_indexedCollections.Contains(_collectionName))
                return;

            var indexModels = new List<CreateIndexModel<EventDocument>>();

            // 1. Composite index: agentId + version (CRITICAL for all queries)
            // This index supports:
            //   - Find by agentId
            //   - Find by agentId + version range
            //   - Sort by version within agentId (DESC for latest queries)
            // Using DESC for version because most queries fetch latest events
            var agentIdVersionIndex = Builders<EventDocument>.IndexKeys
                .Ascending(e => e.AgentId)
                .Descending(e => e.Version);  // ✅ DESC for GetLatestVersion performance
            indexModels.Add(new CreateIndexModel<EventDocument>(
                agentIdVersionIndex,
                new CreateIndexOptions
                {
                    Name = "idx_agentId_version",
                    Unique = true,  // ✅ Enforce uniqueness: one event per (agentId, version)
                    Background = false // Create immediately on startup
                }));

            // 2. Index for timestamp-based cleanup (optional but recommended)
            var timestampIndex = Builders<EventDocument>.IndexKeys
                .Ascending(e => e.Timestamp);
            indexModels.Add(new CreateIndexModel<EventDocument>(
                timestampIndex,
                new CreateIndexOptions
                {
                    Name = "idx_timestamp",
                    Unique = false,
                    Background = false
                }));

            // 3. Index for event type queries (useful for analytics)
            var eventTypeIndex = Builders<EventDocument>.IndexKeys
                .Ascending(e => e.EventType);
            indexModels.Add(new CreateIndexModel<EventDocument>(
                eventTypeIndex,
                new CreateIndexOptions
                {
                    Name = "idx_eventType",
                    Unique = false,
                    Background = false
                }));

            // Batch create all indexes
            await _collection.Indexes.CreateManyAsync(indexModels, ct);

            _indexedCollections.Add(_collectionName);
            
            _logger.LogInformation(
                "MongoDB indexes created for collection '{Collection}': agentId+version DESC (UNIQUE), timestamp, eventType",
                _collectionName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, 
                "Failed to create indexes for collection '{Collection}' (may already exist)",
                _collectionName);
            
            // Mark as indexed even on failure to avoid retry storms
            _indexedCollections.Add(_collectionName);
        }
        finally
        {
            _globalIndexLock.Release();
        }
    }

    public async Task<long> AppendEventsAsync(
        Guid agentId,
        IEnumerable<AgentStateEvent> events,
        CancellationToken ct = default)
    {
        var eventsList = events.ToList();
        if (!eventsList.Any()) return 0;

        // Prepare event documents (events already have version assigned by Grain)
        var eventDocuments = eventsList.Select(evt => new EventDocument
        {
            Id = $"{agentId}_{evt.Version}",
            AgentId = agentId,
            Version = evt.Version,
            EventData = evt.ToByteArray(),
            Timestamp = evt.Timestamp.ToDateTime(),
            EventType = evt.EventData.TypeUrl
        }).ToList();

        // Batch insert to MongoDB
        await _collection.InsertManyAsync(eventDocuments, cancellationToken: ct);

        // ✅ Optimized: Use last element (versions are sequential)
        var newVersion = eventsList[^1].Version;
        
        _logger.LogDebug(
            "Appended {Count} events for agent {AgentId}, version range: {First}-{Last}",
            eventsList.Count, agentId, eventsList[0].Version, newVersion);

        return newVersion;
    }

    public async Task<IReadOnlyList<AgentStateEvent>> GetEventsAsync(
        Guid agentId,
        long? fromVersion = null,
        long? toVersion = null,
        int? maxCount = null,
        CancellationToken ct = default)
    {
        // Build MongoDB query
        var filterBuilder = Builders<EventDocument>.Filter;
        var filter = filterBuilder.Eq(e => e.AgentId, agentId);

        if (fromVersion.HasValue)
            filter &= filterBuilder.Gte(e => e.Version, fromVersion.Value);

        if (toVersion.HasValue)
            filter &= filterBuilder.Lte(e => e.Version, toVersion.Value);

        // Query MongoDB
        // Note: Index is { agentId: 1, version: -1 }, but MongoDB can scan in reverse for ASC sort
        var query = _collection
            .Find(filter)
            .Sort(Builders<EventDocument>.Sort.Ascending(e => e.Version));

        if (maxCount.HasValue)
            query = query.Limit(maxCount.Value);

        var eventDocs = await query.ToListAsync(ct);

        // Deserialize events
        var events = eventDocs
            .Select(doc => AgentStateEvent.Parser.ParseFrom(doc.EventData))
            .ToList();

        _logger.LogDebug(
            "Retrieved {Count} events for agent {AgentId} (version range: {From}-{To})",
            events.Count, agentId, fromVersion, toVersion);

        return events;
    }

    public async Task<long> GetLatestVersionAsync(
        Guid agentId,
        CancellationToken ct = default)
    {
        // ✅ Perfect index usage: { agentId: 1, version: -1 } with DESC sort
        var latestEvent = await _collection
            .Find(e => e.AgentId == agentId)
            .SortByDescending(e => e.Version)
            .Limit(1)
            .FirstOrDefaultAsync(ct);

        return latestEvent?.Version ?? 0;
    }

    public async Task DeleteEventsBeforeVersionAsync(
        Guid agentId,
        long version,
        CancellationToken ct = default)
    {
        var filter = Builders<EventDocument>.Filter.And(
            Builders<EventDocument>.Filter.Eq(e => e.AgentId, agentId),
            Builders<EventDocument>.Filter.Lt(e => e.Version, version)
        );

        var result = await _collection.DeleteManyAsync(filter, ct);

        _logger.LogInformation(
            "Deleted {Count} old events for agent {AgentId} (before version {Version})",
            result.DeletedCount, agentId, version);
    }
}

