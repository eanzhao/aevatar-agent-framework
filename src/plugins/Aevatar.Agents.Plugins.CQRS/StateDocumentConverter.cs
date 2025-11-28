using System.Collections.Concurrent;
using System.Text.Json;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions.CQRS;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Plugins.CQRS;

/// <summary>
/// Shared converter for transforming StateWrapper to StateIndexDocument.
/// Used by both ElasticsearchStateProjector and BatchedStateProjector.
/// </summary>
public class StateDocumentConverter
{
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, Type> _typeCache = new();

    public StateDocumentConverter(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Convert StateWrapper to StateIndexDocument with full state unpacking.
    /// </summary>
    public StateIndexDocument Convert(StateWrapper wrapper)
    {
        var data = new Dictionary<string, object>();

        // Try to resolve and unpack state
        var stateType = ResolveStateType(wrapper.AgentType, wrapper.StateData);
        var state = UnpackState(wrapper.StateData, stateType);

        if (state != null && stateType != null)
        {
            ExtractStateProperties(state, stateType, data);
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

    #region State Resolution

    /// <summary>
    /// Resolve the state type from agent type and state data.
    /// </summary>
    public Type? ResolveStateType(string agentType, Google.Protobuf.WellKnownTypes.Any? stateData)
    {
        if (stateData == null)
            return null;

        // Try to resolve from cached types
        if (_typeCache.TryGetValue(agentType, out var cachedType))
            return cachedType;

        // Try to resolve from Any type URL
        var typeUrl = stateData.TypeUrl;
        if (string.IsNullOrEmpty(typeUrl))
            return null;

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

        _logger.LogWarning(
            "Could not resolve state type for {AgentType} from TypeUrl: {TypeUrl}",
            agentType, typeUrl);

        return null;
    }

    private IMessage? UnpackState(Google.Protobuf.WellKnownTypes.Any? stateData, Type? stateType)
    {
        if (stateData == null || stateType == null || !typeof(IMessage).IsAssignableFrom(stateType))
            return null;

        try
        {
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

            return parser.ParseFrom(stateData.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unpack state of type {TypeName}", stateType?.Name);
            return null;
        }
    }

    #endregion

    #region Property Extraction

    private void ExtractStateProperties(IMessage state, Type stateType, Dictionary<string, object> data)
    {
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
                        WriteIndented = false
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract property {PropertyName} from state", property.Name);
            }
        }
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

