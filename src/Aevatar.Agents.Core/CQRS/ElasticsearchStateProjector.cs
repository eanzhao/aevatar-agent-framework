using System.Collections.Concurrent;
using System.Text.Json;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions.CQRS;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core.CQRS;

/// <summary>
/// Elasticsearch implementation of IStateProjector.
/// Directly projects state changes to Elasticsearch without going through a stream.
/// </summary>
public class ElasticsearchStateProjector : IStateProjector
{
    private readonly IStateIndexService _indexService;
    private readonly ILogger<ElasticsearchStateProjector> _logger;
    private readonly ConcurrentDictionary<string, Type> _typeCache = new();

    public ElasticsearchStateProjector(
        IStateIndexService indexService,
        ILogger<ElasticsearchStateProjector> logger)
    {
        _indexService = indexService;
        _logger = logger;
    }

    public async Task ProjectAsync(StateWrapper wrapper, CancellationToken ct = default)
    {
        try
        {
            _logger.LogDebug(
                "Projecting state for {AgentId} (Type: {AgentType}, Version: {Version})",
                wrapper.AgentId, wrapper.AgentType, wrapper.Version);

            // 1. Resolve state type and unpack state data
            var stateType = ResolveStateType(wrapper.AgentType, wrapper.StateData);
            var state = UnpackState(wrapper.StateData, stateType);

            // 2. Ensure index exists with proper mapping
            await _indexService.EnsureIndexExistsAsync(wrapper.AgentType, stateType, ct);

            // 3. Convert to index document
            var document = ConvertToIndexDocument(wrapper, state, stateType);

            // 4. Index to Elasticsearch
            await _indexService.IndexStateAsync(document, ct);

            _logger.LogDebug(
                "Successfully projected state for {AgentId}, version: {Version}",
                wrapper.AgentId, wrapper.Version);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error projecting state for {AgentId} (Type: {AgentType})",
                wrapper.AgentId, wrapper.AgentType);
            throw;
        }
    }

    #region State Resolution

    private Type? ResolveStateType(string agentType, Google.Protobuf.WellKnownTypes.Any stateData)
    {
        // Try to resolve from cached types
        if (_typeCache.TryGetValue(agentType, out var cachedType))
            return cachedType;

        // Try to resolve from Any type URL
        var typeUrl = stateData.TypeUrl;
        if (!string.IsNullOrEmpty(typeUrl))
        {
            // Format: type.googleapis.com/package.TypeName
            var typeName = typeUrl.Contains('/')
                ? typeUrl.Substring(typeUrl.LastIndexOf('/') + 1)
                : typeUrl;

            // Search all loaded assemblies for the type
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Array.Empty<Type>(); }
                })
                .FirstOrDefault(t =>
                    t.FullName == typeName ||
                    t.Name == typeName ||
                    (typeof(IMessage).IsAssignableFrom(t) && t.Name == typeName));

            if (type != null)
            {
                _typeCache[agentType] = type;
                return type;
            }
        }

        _logger.LogWarning(
            "Could not resolve state type for {AgentType} from TypeUrl: {TypeUrl}",
            agentType, typeUrl);

        return null;
    }

    private IMessage? UnpackState(Google.Protobuf.WellKnownTypes.Any stateData, Type? stateType)
    {
        if (stateType == null || !typeof(IMessage).IsAssignableFrom(stateType))
            return null;

        try
        {
            // Get the parser for the message type
            var parserProperty = stateType.GetProperty("Parser",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

            if (parserProperty == null)
            {
                _logger.LogWarning("No Parser property found on type {TypeName}", stateType.Name);
                return null;
            }

            var parser = parserProperty.GetValue(null) as MessageParser;
            if (parser == null)
            {
                _logger.LogWarning("Parser is null for type {TypeName}", stateType.Name);
                return null;
            }

            // Parse the Any value directly using the parser
            return parser.ParseFrom(stateData.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unpack state of type {TypeName}", stateType?.Name);
            return null;
        }
    }

    #endregion

    #region Document Conversion

    private StateIndexDocument ConvertToIndexDocument(StateWrapper wrapper, IMessage? state, Type? stateType)
    {
        var data = new Dictionary<string, object>();

        if (state != null && stateType != null)
        {
            // Extract properties from the Protobuf message
            foreach (var property in stateType.GetProperties())
            {
                // Skip Protobuf internal properties
                if (property.Name == "Parser" ||
                    property.Name == "Descriptor" ||
                    property.Name == "MessageType" ||
                    property.DeclaringType == typeof(IMessage) ||
                    property.DeclaringType == typeof(object))
                    continue;

                try
                {
                    var value = property.GetValue(state);
                    if (value == null)
                        continue;

                    var propertyName = ToCamelCase(property.Name);

                    if (IsBasicType(property.PropertyType))
                    {
                        // Handle Timestamp specially
                        if (value is Google.Protobuf.WellKnownTypes.Timestamp timestamp)
                        {
                            data[propertyName] = timestamp.ToDateTime();
                        }
                        else
                        {
                            data[propertyName] = value;
                        }
                    }
                    else
                    {
                        // Complex types: serialize to JSON using System.Text.Json
                        data[propertyName] = JsonSerializer.Serialize(value, new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                            WriteIndented = false,
                            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to extract property {PropertyName} from state",
                        property.Name);
                }
            }
        }

        return new StateIndexDocument
        {
            AgentId = wrapper.AgentId,
            AgentType = wrapper.AgentType,
            Data = data,
            Version = wrapper.Version,
            IndexedAt = wrapper.PublishedAt?.ToDateTime() ?? DateTime.UtcNow
        };
    }

    private static bool IsBasicType(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        if (underlyingType.IsPrimitive)
            return true;

        if (underlyingType == typeof(string) ||
            underlyingType == typeof(DateTime) ||
            underlyingType == typeof(DateTimeOffset) ||
            underlyingType == typeof(decimal) ||
            underlyingType == typeof(Guid) ||
            underlyingType == typeof(Google.Protobuf.WellKnownTypes.Timestamp))
            return true;

        return false;
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    #endregion
}

