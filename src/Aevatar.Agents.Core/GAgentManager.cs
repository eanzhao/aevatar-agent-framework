using System.Collections.Concurrent;
using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core.EventSourcing;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core;

/// <summary>
/// Default implementation of Agent Manager.
/// Responsible for Agent type discovery, registration, and metadata management.
/// </summary>
public class GAgentManager : IGAgentManager
{
    private readonly ILogger<GAgentManager> _logger;
    private readonly ConcurrentDictionary<Type, AgentTypeMetadata> _agentTypes = new();
    private readonly ConcurrentDictionary<Type, bool> _eventTypes = new();
    private readonly object _lock = new();

    public GAgentManager(ILogger<GAgentManager> logger)
    {
        _logger = logger;

        // Scan types in the current assembly during initialization
        DiscoverTypesInLoadedAssemblies();
    }

    #region Type Discovery

    public List<Type> GetAvailableAgentTypes()
    {
        return _agentTypes.Keys.ToList();
    }

    public List<Type> GetAvailableEventTypes()
    {
        return _eventTypes.Keys.ToList();
    }

    public List<Type> GetSupportedEventTypes<TAgent>() where TAgent : IGAgent
    {
        return GetSupportedEventTypes(typeof(TAgent));
    }

    public List<Type> GetSupportedEventTypes(Type agentType)
    {
        if (_agentTypes.TryGetValue(agentType, out var metadata))
        {
            return metadata.SupportedEventTypes;
        }

        // If no cached metadata, analyze dynamically
        return AnalyzeSupportedEventTypes(agentType);
    }

    public bool IsValidAgentType(Type type)
    {
        return typeof(IGAgent).IsAssignableFrom(type)
               && type.IsClass
               && !type.IsAbstract;
    }

    public bool IsValidEventType(Type type)
    {
        return type.IsClass
               && !type.IsAbstract
               && (type.Name.EndsWith("Event") || type.Name.EndsWith("Message"));
    }

    #endregion

    #region Type Registration

    public void RegisterAgentType(Type agentType)
    {
        if (!IsValidAgentType(agentType))
        {
            throw new ArgumentException($"Type {agentType.Name} is not a valid agent type");
        }

        var metadata = CreateAgentMetadata(agentType);
        if (_agentTypes.TryAdd(agentType, metadata))
        {
            _logger.LogInformation("Registered agent type {AgentType}", agentType.Name);
        }
    }

    public void UnregisterAgentType(Type agentType)
    {
        if (_agentTypes.TryRemove(agentType, out _))
        {
            _logger.LogInformation("Unregistered agent type {AgentType}", agentType.Name);
        }
    }

    public void RegisterEventType(Type eventType)
    {
        if (!IsValidEventType(eventType))
        {
            throw new ArgumentException($"Type {eventType.Name} is not a valid event type");
        }

        if (_eventTypes.TryAdd(eventType, true))
        {
            _logger.LogDebug("Registered event type {EventType}", eventType.Name);
        }
    }

    public void UnregisterEventType(Type eventType)
    {
        if (_eventTypes.TryRemove(eventType, out _))
        {
            _logger.LogDebug("Unregistered event type {EventType}", eventType.Name);
        }
    }

    #endregion

    #region Metadata

    public AgentTypeMetadata? GetAgentMetadata(Type agentType)
    {
        return _agentTypes.TryGetValue(agentType, out var metadata) ? metadata : null;
    }

    public AgentTypeMetadata? GetAgentMetadata<TAgent>() where TAgent : IGAgent
    {
        return GetAgentMetadata(typeof(TAgent));
    }

    public IReadOnlyList<AgentTypeMetadata> GetAllAgentMetadata()
    {
        return _agentTypes.Values.ToList();
    }

    #endregion

    #region Plugin Support

    public int LoadAgentTypesFromAssembly(Assembly assembly)
    {
        _logger.LogInformation("Loading agent types from assembly {AssemblyName}", assembly.FullName);

        var count = 0;

        try
        {
            var types = assembly.GetTypes();

            // Load Agent types
            foreach (var type in types.Where(IsValidAgentType))
            {
                RegisterAgentType(type);
                count++;
            }

            // Load Event types
            foreach (var type in types.Where(IsValidEventType))
            {
                RegisterEventType(type);
            }
        }
        catch (ReflectionTypeLoadException ex)
        {
            _logger.LogWarning(ex, "Failed to load some types from assembly {AssemblyName}", assembly.FullName);

            // Try to load accessible types
            if (ex.Types != null)
            {
                foreach (var type in ex.Types.Where(t => t != null))
                {
                    try
                    {
                        if (IsValidAgentType(type!))
                        {
                            RegisterAgentType(type!);
                            count++;
                        }
                        else if (IsValidEventType(type!))
                        {
                            RegisterEventType(type!);
                        }
                    }
                    catch (Exception innerEx)
                    {
                        _logger.LogDebug(innerEx, "Failed to register type {TypeName}", type!.Name);
                    }
                }
            }
        }

        _logger.LogInformation("Loaded {Count} agent types from assembly {AssemblyName}", count, assembly.FullName);
        return count;
    }

    public int LoadAgentTypesFromPath(string assemblyPath)
    {
        try
        {
            var assembly = Assembly.LoadFrom(assemblyPath);
            return LoadAgentTypesFromAssembly(assembly);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load assembly from path {AssemblyPath}", assemblyPath);
            return 0;
        }
    }

    public void UnloadAgentTypesFromAssembly(Assembly assembly)
    {
        var typesToRemove = _agentTypes.Keys.Where(t => t.Assembly == assembly).ToList();

        foreach (var type in typesToRemove)
        {
            UnregisterAgentType(type);
        }

        var eventTypesToRemove = _eventTypes.Keys.Where(t => t.Assembly == assembly).ToList();

        foreach (var type in eventTypesToRemove)
        {
            UnregisterEventType(type);
        }

        _logger.LogInformation(
            "Unloaded {AgentCount} agent types and {EventCount} event types from assembly {AssemblyName}",
            typesToRemove.Count, eventTypesToRemove.Count, assembly.FullName);
    }

    #endregion

    #region Private Methods

    private void DiscoverTypesInLoadedAssemblies()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in assemblies)
        {
            // Skip system assemblies
            if (assembly.FullName?.StartsWith("System") == true ||
                assembly.FullName?.StartsWith("Microsoft") == true)
            {
                continue;
            }

            try
            {
                LoadAgentTypesFromAssembly(assembly);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to scan assembly {AssemblyName}", assembly.FullName);
            }
        }
    }

    private AgentTypeMetadata CreateAgentMetadata(Type agentType)
    {
        var stateType = ExtractStateType(agentType);
        var supportedEvents = AnalyzeSupportedEventTypes(agentType);
        var supportsEventSourcing = typeof(GAgentBaseWithEventSourcing<>).IsAssignableFrom(agentType);
        var supportsConfiguration = IsConfigurationAgent(agentType);

        return new AgentTypeMetadata
        {
            AgentType = agentType,
            Name = agentType.Name,
            Description = GetTypeDescription(agentType),
            StateType = stateType,
            SupportedEventTypes = supportedEvents,
            SupportsEventSourcing = supportsEventSourcing,
            SupportsConfiguration = supportsConfiguration,
            AssemblyName = agentType.Assembly.FullName,
            RegisteredAt = DateTimeOffset.UtcNow
        };
    }

    private Type? ExtractStateType(Type agentType)
    {
        // Find IStateGAgent<TState> interface
        var stateInterface = agentType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStateGAgent<>));

        return stateInterface?.GetGenericArguments().FirstOrDefault();
    }

    private bool IsConfigurationAgent(Type agentType)
    {
        // Check if inherits from GAgentBase<TState, TConfig> (2 generic parameters)
        var baseType = agentType.BaseType;
        while (baseType != null && baseType != typeof(object))
        {
            if (baseType.IsGenericType &&
                baseType.GetGenericTypeDefinition() == typeof(GAgentBase<,>) &&
                baseType.GetGenericArguments().Length == 2)
            {
                return true;
            }
            baseType = baseType.BaseType;
        }
        return false;
    }

    private List<Type> AnalyzeSupportedEventTypes(Type agentType)
    {
        var supportedEvents = new List<Type>();

        // Find all methods with EventHandler attribute
        var methods = agentType.GetMethods(
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.Instance |
            BindingFlags.DeclaredOnly);

        // Also check base class methods
        var baseType = agentType.BaseType;
        while (baseType != null && baseType != typeof(object))
        {
            methods = methods.Concat(baseType.GetMethods(
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance |
                BindingFlags.DeclaredOnly)).ToArray();
            baseType = baseType.BaseType;
        }

        foreach (var method in methods)
        {
            var eventHandlerAttr = method.GetCustomAttribute<EventHandlerAttribute>();
            if (eventHandlerAttr != null)
            {
                var parameters = method.GetParameters();
                if (parameters.Length > 0)
                {
                    var eventType = parameters[0].ParameterType;
                    if (!supportedEvents.Contains(eventType))
                    {
                        supportedEvents.Add(eventType);
                    }
                }
            }
        }

        return supportedEvents;
    }

    private string? GetTypeDescription(Type type)
    {
        // Try to get description from XML documentation comments or attributes
        // Simplified here, can integrate XML documentation reading in practice
        var descriptionAttr = type.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>();
        return descriptionAttr?.Description;
    }

    #endregion
}




