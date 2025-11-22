using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core.EventSourcing;

/// <summary>
/// Resolves Protobuf event types using reflection and caching.
/// </summary>
public class ProtobufEventTypeResolver : IEventTypeResolver
{
    private readonly ILogger<ProtobufEventTypeResolver>? _logger;
    
    // Key: Simple type name (e.g., "MoneyDeposited")
    // Value: Cached parser and metadata
    private readonly ConcurrentDictionary<string, EventTypeInfo> _typeCache = new();

    public ProtobufEventTypeResolver(ILogger<ProtobufEventTypeResolver>? logger = null)
    {
        _logger = logger;
    }

    public EventTypeInfo? Resolve(string typeUrl, Assembly searchAssembly)
    {
        var simpleTypeName = ExtractSimpleTypeName(typeUrl);

        // Fast path: cache hit
        if (_typeCache.TryGetValue(simpleTypeName, out var info))
        {
            return info;
        }

        // Slow path: build and cache
        info = BuildTypeCache(simpleTypeName, searchAssembly);
        if (info != null)
        {
            _typeCache[simpleTypeName] = info;
            _logger?.LogInformation("Type {TypeName} cached. Total cached types: {Count}", simpleTypeName, _typeCache.Count);
        }

        return info;
    }

    private EventTypeInfo? BuildTypeCache(string simpleTypeName, Assembly assembly)
    {
        try
        {
            var matchingType = assembly.GetTypes()
                .FirstOrDefault(t => t.Name == simpleTypeName && typeof(IMessage).IsAssignableFrom(t));

            if (matchingType == null)
            {
                _logger?.LogWarning("Type {TypeName} not found in assembly {Assembly}", simpleTypeName, assembly.FullName);
                return null;
            }

            var parser = matchingType
                .GetProperty("Parser", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null) as MessageParser;

            if (parser == null)
            {
                _logger?.LogWarning("Parser property not found for type {TypeName}", matchingType.FullName);
                return null;
            }

            _logger?.LogDebug("Built type cache for {TypeName} (type: {FullName})", simpleTypeName, matchingType.FullName);

            return new EventTypeInfo(matchingType, parser);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error building type cache for {TypeName}", simpleTypeName);
            return null;
        }
    }

    private static string ExtractSimpleTypeName(string typeUrl)
    {
        var fullTypeName = typeUrl.Substring(typeUrl.LastIndexOf('/') + 1);
        return fullTypeName.Contains('.')
            ? fullTypeName.Substring(fullTypeName.LastIndexOf('.') + 1)
            : fullTypeName;
    }
    
    public void ClearCache()
    {
        _typeCache.Clear();
    }
}
