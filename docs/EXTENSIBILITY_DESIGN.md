# Aevatar Agent Framework - 扩展性设计

## 🎯 扩展性设计概述

Aevatar Agent Framework采用**插件式架构**设计，提供了丰富的扩展点，支持在**不修改核心框架代码**的情况下扩展功能。扩展性设计遵循**开放封闭原则**（OCP），对扩展开放，对修改封闭。

## 🏗️ 扩展性架构分层

```
┌─────────────────────────────────────────────────────────┐
│                应用扩展层                                │
│  ┌───────────────────────────────────────────────────┐   │
│  │Custom Agents │Custom Tools    │Custom Strategies │   │
│  │AI Providers  │Event Handlers  │Processing Logic  │   │
│  └───────────────────────────────────────────────────┘   │
├─────────────────────────────────────────────────────────┤
│                框架扩展点                                │
│  ┌──────────────┬──────────────┬────────────────────┐   │
│  │Extension    │Plugin       │Module             │   │
│  │Points       │System       │System             │   │
│  └──────────────┴──────────────┴────────────────────┘   │
├─────────────────────────────────────────────────────────┤
│                核心抽象层                                │
│  ┌──────────────┬──────────────┬────────────────────┐   │
│  │Interfaces   │Base Classes │Abstractions       │   │
│  │Contracts    │Templates    │Protocols          │   │
│  └──────────────┴──────────────┴────────────────────┘   │
├─────────────────────────────────────────────────────────┤
│                依赖注入与发现                            │
│  ┌──────────────┬──────────────┬────────────────────┐   │
│  │DI Container │Service      │Auto-Discovery     │   │
│  │Registration │Locator      │& Registration      │   │
│  └──────────────┴──────────────┴────────────────────┘   │
├─────────────────────────────────────────────────────────┤
│                配置与元数据                              │
│  ┌──────────────┬──────────────┬────────────────────┐   │
│  │Configuration│Metadata     │Convention         │   │
│  │System       │Attributes   │Based Setup        │   │
│  └──────────────┴──────────────┴────────────────────┘   │
└─────────────────────────────────────────────────────────┘
```

## 🔧 核心扩展接口

### 1. 扩展点标记接口

```csharp
// 扩展点基础接口
public interface IExtensionPoint
{
    string ExtensionPointName { get; }
    string ExtensionPointDescription { get; }
    Version ExtensionPointVersion { get; }
}

// 可扩展接口标记
public interface IExtensible<out TExtension> where TExtension : IExtension
{
    IEnumerable<TExtension> Extensions { get; }
    Task LoadExtensionsAsync(CancellationToken cancellationToken = default);
    Task UnloadExtensionsAsync(CancellationToken cancellationToken = default);
}

// 扩展接口
public interface IExtension
{
    string ExtensionId { get; }
    string ExtensionName { get; }
    string ExtensionDescription { get; }
    Version ExtensionVersion { get; }
    string ExtensionAuthor { get; }
    ExtensionPriority Priority { get; }
    ExtensionState State { get; }

    Task InitializeAsync(IExtensionContext context, CancellationToken cancellationToken = default);
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}

// 扩展状态
public enum ExtensionState
{
    NotLoaded,
    Loading,
    Loaded,
    Initializing,
    Initialized,
    Faulted,
    ShuttingDown,
    Shutdown
}

// 扩展优先级
public enum ExtensionPriority
{
    Lowest = 0,
    VeryLow = 25,
    Low = 50,
    BelowNormal = 75,
    Normal = 100,
    AboveNormal = 125,
    High = 150,
    VeryHigh = 175,
    Highest = 200
}
```

### 2. 扩展上下文

```csharp
public interface IExtensionContext
{
    // 基础服务
    IServiceProvider ServiceProvider { get; }
    IConfiguration Configuration { get; }
    ILogger Logger { get; }

    // 扩展信息
    IExtension Extension { get; }
    ExtensionAssemblyInfo AssemblyInfo { get; }

    // 配置访问
    TConfig GetConfiguration<TConfig>(string sectionName = null) where TConfig : class, new();
    object GetConfiguration(string key);

    // 服务注册
    void RegisterService<TService, TImplementation>(ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TService : class
        where TImplementation : class, TService;

    void RegisterService<TService>(Func<IServiceProvider, TService> factory, ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TService : class;

    // 事件订阅
    void SubscribeToEvent<TEvent>(Func<TEvent, Task> handler) where TEvent : class;
    void SubscribeToEvent<TEvent>(Action<TEvent> handler) where TEvent : class;

    // 资源访问
    string GetResourcePath(string resourceName);
    Stream GetResourceStream(string resourceName);
    string ReadResourceText(string resourceName);

    // 日志记录
    void LogInformation(string message, params object[] args);
    void LogWarning(string message, params object[] args);
    void LogError(Exception exception, string message, params object[] args);
}

// 扩展程序集信息
public class ExtensionAssemblyInfo
{
    public string AssemblyName { get; init; }
    public string AssemblyVersion { get; init; }
    public string AssemblyLocation { get; init; }
    public DateTime AssemblyLastModified { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}
```

## 🧩 扩展发现与加载

### 1. 扩展发现器

```csharp
public interface IExtensionDiscovery
{
    Task<List<ExtensionInfo>> DiscoverExtensionsAsync(CancellationToken cancellationToken = default);
    Task<List<ExtensionInfo>> DiscoverExtensionsAsync(string searchPattern, CancellationToken cancellationToken = default);
    Task<List<ExtensionInfo>> DiscoverExtensionsAsync(Assembly assembly, CancellationToken cancellationToken = default);
    Task<List<ExtensionInfo>> DiscoverExtensionsAsync(string directory, string searchPattern = "*.dll", CancellationToken cancellationToken = default);
}

public class ExtensionDiscovery : IExtensionDiscovery
{
    private readonly ILogger<ExtensionDiscovery> _logger;
    private readonly List<IExtensionFilter> _filters;

    public ExtensionDiscovery(ILogger<ExtensionDiscovery> logger)
    {
        _logger = logger;
        _filters = new List<IExtensionFilter>
        {
            new ExtensionTypeFilter(),
            new ExtensionMetadataFilter(),
            new ExtensionDependencyFilter()
        };
    }

    public async Task<List<ExtensionInfo>> DiscoverExtensionsAsync(CancellationToken cancellationToken = default)
    {
        var extensionInfos = new List<ExtensionInfo>();

        // 1. 发现程序集中的扩展
        var assemblyExtensions = await DiscoverAssemblyExtensionsAsync(cancellationToken);
        extensionInfos.AddRange(assemblyExtensions);

        // 2. 发现目录中的扩展
        var directoryExtensions = await DiscoverDirectoryExtensionsAsync(cancellationToken);
        extensionInfos.AddRange(directoryExtensions);

        // 3. 发现NuGet包中的扩展
        var nugetExtensions = await DiscoverNuGetExtensionsAsync(cancellationToken);
        extensionInfos.AddRange(nugetExtensions);

        // 4. 应用过滤器
        var filteredExtensions = await ApplyFiltersAsync(extensionInfos, cancellationToken);

        _logger.LogInformation("Discovered {ExtensionCount} extensions", filteredExtensions.Count);

        return filteredExtensions;
    }

    private async Task<List<ExtensionInfo>> DiscoverAssemblyExtensionsAsync(CancellationToken cancellationToken)
    {
        var extensions = new List<ExtensionInfo>();

        // 获取当前应用程序域中的所有程序集
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !a.FullName.StartsWith("System."))
            .ToList();

        foreach (var assembly in assemblies)
        {
            try
            {
                var assemblyExtensions = await DiscoverExtensionsInAssemblyAsync(assembly, cancellationToken);
                extensions.AddRange(assemblyExtensions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error discovering extensions in assembly {AssemblyName}", assembly.FullName);
            }
        }

        return extensions;
    }

    private async Task<List<ExtensionInfo>> DiscoverExtensionsInAssemblyAsync(Assembly assembly, CancellationToken cancellationToken)
    {
        var extensions = new List<ExtensionInfo>();

        // 查找标记有ExtensionAttribute的类型
        var extensionTypes = assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<ExtensionAttribute>() != null)
            .Where(t => typeof(IExtension).IsAssignableFrom(t))
            .ToList();

        foreach (var extensionType in extensionTypes)
        {
            try
            {
                var extensionInfo = CreateExtensionInfo(extensionType, assembly);
                if (extensionInfo != null)
                {
                    extensions.Add(extensionInfo);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error creating extension info for type {TypeName}", extensionType.Name);
            }
        }

        await Task.CompletedTask;
        return extensions;
    }

    private ExtensionInfo CreateExtensionInfo(Type extensionType, Assembly assembly)
    {
        var extensionAttr = extensionType.GetCustomAttribute<ExtensionAttribute>();
        if (extensionAttr == null)
        {
            return null;
        }

        var metadata = new Dictionary<string, object>();

        // 提取元数据
        var metadataAttrs = extensionType.GetCustomAttributes<ExtensionMetadataAttribute>();
        foreach (var metadataAttr in metadataAttrs)
        {
            metadata[metadataAttr.Key] = metadataAttr.Value;
        }

        return new ExtensionInfo
        {
            ExtensionId = extensionAttr.Id ?? extensionType.FullName,
            ExtensionType = extensionType,
            ExtensionName = extensionAttr.Name ?? extensionType.Name,
            ExtensionDescription = extensionAttr.Description,
            ExtensionVersion = extensionAttr.Version != null ? Version.Parse(extensionAttr.Version) : new Version(1, 0, 0),
            ExtensionAuthor = extensionAttr.Author,
            Priority = extensionAttr.Priority,
            Assembly = assembly,
            Metadata = metadata,
            Dependencies = DiscoverDependencies(extensionType),
            Capabilities = DiscoverCapabilities(extensionType)
        };
    }

    private List<ExtensionDependency> DiscoverDependencies(Type extensionType)
    {
        var dependencies = new List<ExtensionDependency>();

        var dependencyAttrs = extensionType.GetCustomAttributes<ExtensionDependencyAttribute>();
        foreach (var dependencyAttr in dependencyAttrs)
        {
            dependencies.Add(new ExtensionDependency
            {
                DependencyId = dependencyAttr.DependencyId,
                DependencyType = dependencyAttr.DependencyType,
                MinimumVersion = dependencyAttr.MinimumVersion != null ? Version.Parse(dependencyAttr.MinimumVersion) : null,
                MaximumVersion = dependencyAttr.MaximumVersion != null ? Version.Parse(dependencyAttr.MaximumVersion) : null,
                IsRequired = dependencyAttr.IsRequired
            });
        }

        return dependencies;
    }

    private List<string> DiscoverCapabilities(Type extensionType)
    {
        var capabilities = new List<string>();

        var capabilityAttrs = extensionType.GetCustomAttributes<ExtensionCapabilityAttribute>();
        foreach (var capabilityAttr in capabilityAttrs)
        {
            capabilities.Add(capabilityAttr.Capability);
        }

        return capabilities;
    }
}
```

### 2. 扩展信息模型

```csharp
public class ExtensionInfo
{
    public string ExtensionId { get; init; }
    public Type ExtensionType { get; init; }
    public string ExtensionName { get; init; }
    public string ExtensionDescription { get; init; }
    public Version ExtensionVersion { get; init; }
    public string ExtensionAuthor { get; init; }
    public ExtensionPriority Priority { get; init; }
    public Assembly Assembly { get; init; }

    public List<ExtensionDependency> Dependencies { get; init; } = new();
    public List<string> Capabilities { get; init; } = new();
    public Dictionary<string, object> Metadata { get; init; } = new();

    public bool IsEnabled { get; set; } = true;
    public ExtensionState State { get; set; } = ExtensionState.NotLoaded;
    public DateTime? LoadTime { get; set; }
    public Exception LoadError { get; set; }
}

public class ExtensionDependency
{
    public string DependencyId { get; init; }
    public ExtensionDependencyType DependencyType { get; init; }
    public Version MinimumVersion { get; init; }
    public Version MaximumVersion { get; init; }
    public bool IsRequired { get; init; }
}

public enum ExtensionDependencyType
{
    Extension,
    Assembly,
    Package,
    Service,
    Framework
}
```

### 3. 扩展加载器

```csharp
public interface IExtensionLoader
{
    Task<IExtension> LoadExtensionAsync(ExtensionInfo extensionInfo, IExtensionContext context, CancellationToken cancellationToken = default);
    Task UnloadExtensionAsync(IExtension extension, CancellationToken cancellationToken = default);
    Task<bool> CanLoadExtensionAsync(ExtensionInfo extensionInfo);
}

public class ExtensionLoader : IExtensionLoader
{
    private readonly ILogger<ExtensionLoader> _logger;
    private readonly ConcurrentDictionary<string, IExtension> _loadedExtensions;

    public ExtensionLoader(ILogger<ExtensionLoader> logger)
    {
        _logger = logger;
        _loadedExtensions = new ConcurrentDictionary<string, IExtension>();
    }

    public async Task<IExtension> LoadExtensionAsync(ExtensionInfo extensionInfo, IExtensionContext context, CancellationToken cancellationToken = default)
    {
        if (extensionInfo.State != ExtensionState.NotLoaded)
        {
            throw new InvalidOperationException($"Extension {extensionInfo.ExtensionId} is already loaded");
        }

        _logger.LogInformation("Loading extension {ExtensionId}", extensionInfo.ExtensionId);

        try
        {
            extensionInfo.State = ExtensionState.Loading;

            // 1. 验证依赖
            await ValidateDependenciesAsync(extensionInfo, cancellationToken);

            // 2. 创建扩展实例
            var extension = CreateExtensionInstance(extensionInfo);

            // 3. 初始化扩展
            extensionInfo.State = ExtensionState.Initializing;
            await extension.InitializeAsync(context, cancellationToken);

            // 4. 注册扩展
            _loadedExtensions[extensionInfo.ExtensionId] = extension;

            extensionInfo.State = ExtensionState.Initialized;
            extensionInfo.LoadTime = DateTime.UtcNow;

            _logger.LogInformation("Extension {ExtensionId} loaded successfully", extensionInfo.ExtensionId);

            return extension;
        }
        catch (Exception ex)
        {
            extensionInfo.State = ExtensionState.Faulted;
            extensionInfo.LoadError = ex;

            _logger.LogError(ex, "Failed to load extension {ExtensionId}", extensionInfo.ExtensionId);
            throw;
        }
    }

    private IExtension CreateExtensionInstance(ExtensionInfo extensionInfo)
    {
        try
        {
            // 查找合适的构造函数
            var constructors = extensionInfo.ExtensionType.GetConstructors();
            var constructor = constructors.OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();

            if (constructor == null)
            {
                throw new InvalidOperationException($"No suitable constructor found for {extensionInfo.ExtensionType.Name}");
            }

            // 创建参数
            var parameters = new object[constructor.GetParameters().Length];
            var serviceProvider = context.ServiceProvider;

            for (int i = 0; i < parameters.Length; i++)
            {
                var parameter = constructor.GetParameters()[i];
                parameters[i] = serviceProvider.GetService(parameter.ParameterType);
            }

            // 创建实例
            var extension = constructor.Invoke(parameters) as IExtension;
            if (extension == null)
            {
                throw new InvalidOperationException($"Failed to create extension instance of type {extensionInfo.ExtensionType.Name}");
            }

            return extension;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating extension instance for {ExtensionId}", extensionInfo.ExtensionId);
            throw;
        }
    }

    private async Task ValidateDependenciesAsync(ExtensionInfo extensionInfo, CancellationToken cancellationToken)
    {
        foreach (var dependency in extensionInfo.Dependencies)
        {
            if (!await ValidateDependencyAsync(dependency, cancellationToken))
            {
                if (dependency.IsRequired)
                {
                    throw new ExtensionLoadException($"Required dependency {dependency.DependencyId} not satisfied for extension {extensionInfo.ExtensionId}");
                }
                else
                {
                    _logger.LogWarning("Optional dependency {DependencyId} not satisfied for extension {ExtensionId}",
                        dependency.DependencyId, extensionInfo.ExtensionId);
                }
            }
        }
    }

    private async Task<bool> ValidateDependencyAsync(ExtensionDependency dependency, CancellationToken cancellationToken)
    {
        return dependency.DependencyType switch
        {
            ExtensionDependencyType.Extension => await ValidateExtensionDependencyAsync(dependency, cancellationToken),
            ExtensionDependencyType.Assembly => ValidateAssemblyDependency(dependency),
            ExtensionDependencyType.Package => await ValidatePackageDependencyAsync(dependency, cancellationToken),
            ExtensionDependencyType.Service => ValidateServiceDependency(dependency),
            ExtensionDependencyType.Framework => ValidateFrameworkDependency(dependency),
            _ => false
        };
    }

    private async Task<bool> ValidateExtensionDependencyAsync(ExtensionDependency dependency, CancellationToken cancellationToken)
    {
        // 检查扩展是否已加载
        if (_loadedExtensions.TryGetValue(dependency.DependencyId, out var loadedDependency))
        {
            // 检查版本兼容性
            var dependencyVersion = loadedExtension.GetType().Assembly.GetName().Version;
            return IsVersionCompatible(dependencyVersion, dependency.MinimumVersion, dependency.MaximumVersion);
        }

        await Task.CompletedTask;
        return false;
    }

    private bool ValidateAssemblyDependency(ExtensionDependency dependency)
    {
        try
        {
            var assembly = Assembly.Load(dependency.DependencyId);
            var assemblyVersion = assembly.GetName().Version;
            return IsVersionCompatible(assemblyVersion, dependency.MinimumVersion, dependency.MaximumVersion);
        }
        catch
        {
            return false;
        }
    }

    private bool IsVersionCompatible(Version currentVersion, Version minimumVersion, Version maximumVersion)
    {
        if (minimumVersion != null && currentVersion < minimumVersion)
        {
            return false;
        }

        if (maximumVersion != null && currentVersion > maximumVersion)
        {
            return false;
        }

        return true;
    }
}
```

## 🧪 具体扩展点

### 1. AI提供程序扩展

```csharp
// AI提供程序扩展接口
public interface IAIProviderExtension : IExtension
{
    IAevatarLLMProvider CreateProvider(IServiceProvider serviceProvider);
    bool SupportsModel(string modelName);
    bool SupportsCapability(AIProviderCapability capability);
    Task<ModelInfo> GetModelInfoAsync(string modelName, CancellationToken cancellationToken = default);
}

// AI提供程序扩展实现
[Extension(
    id: "openai-provider",
    name: "OpenAI Provider Extension",
    description: "Provides OpenAI GPT model integration",
    version: "1.0.0",
    author: "Aevatar Team",
    priority: ExtensionPriority.Normal
)]
[ExtensionCapability("ai-provider")]
[ExtensionCapability("llm-support")]
[ExtensionCapability("openai-models")]
public class OpenAIProviderExtension : IAIProviderExtension
{
    private readonly ILogger<OpenAIProviderExtension> _logger;
    private ExtensionContext _context;

    public OpenAIProviderExtension(ILogger<OpenAIProviderExtension> logger)
    {
        _logger = logger;
    }

    public string ExtensionId => "openai-provider";
    public string ExtensionName => "OpenAI Provider Extension";
    public string ExtensionDescription => "Provides OpenAI GPT model integration";
    public Version ExtensionVersion => new Version(1, 0, 0);
    public string ExtensionAuthor => "Aevatar Team";
    public ExtensionPriority Priority => ExtensionPriority.Normal;
    public ExtensionState State { get; private set; }

    public async Task InitializeAsync(IExtensionContext context, CancellationToken cancellationToken = default)
    {
        _context = context as ExtensionContext;
        State = ExtensionState.Initializing;

        _logger.LogInformation("Initializing OpenAI Provider Extension");

        try
        {
            // 注册服务
            context.RegisterService<IAevatarLLMProvider, OpenAIProvider>();

            // 注册模型信息
            RegisterModelInformation(context);

            State = ExtensionState.Initialized;
            _logger.LogInformation("OpenAI Provider Extension initialized successfully");
        }
        catch (Exception ex)
        {
            State = ExtensionState.Faulted;
            _logger.LogError(ex, "Failed to initialize OpenAI Provider Extension");
            throw;
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        State = ExtensionState.ShuttingDown;
        _logger.LogInformation("Shutting down OpenAI Provider Extension");

        // 清理资源
        await Task.CompletedTask;

        State = ExtensionState.Shutdown;
    }

    public IAevatarLLMProvider CreateProvider(IServiceProvider serviceProvider)
    {
        return serviceProvider.GetRequiredService<IAevatarLLMProvider>();
    }

    public bool SupportsModel(string modelName)
    {
        var supportedModels = new[]
        {
            "gpt-4", "gpt-4-turbo", "gpt-4-turbo-preview",
            "gpt-3.5-turbo", "gpt-3.5-turbo-16k",
            "text-davinci-003", "text-curie-001"
        };

        return supportedModels.Contains(modelName, StringComparer.OrdinalIgnoreCase);
    }

    public bool SupportsCapability(AIProviderCapability capability)
    {
        return capability switch
        {
            AIProviderCapability.Chat => true,
            AIProviderCapability.Streaming => true,
            AIProviderCapability.ToolCalling => true,
            AIProviderCapability.FunctionCalling => true,
            _ => false
        };
    }

    public async Task<ModelInfo> GetModelInfoAsync(string modelName, CancellationToken cancellationToken = default)
    {
        if (!SupportsModel(modelName))
        {
            throw new NotSupportedException($"Model {modelName} is not supported by this provider");
        }

        var modelInfo = new ModelInfo
        {
            ModelName = modelName,
            Provider = "OpenAI",
            Capabilities = GetModelCapabilities(modelName),
            MaxTokens = GetModelMaxTokens(modelName),
            Pricing = GetModelPricing(modelName)
        };

        return modelInfo;
    }

    private void RegisterModelInformation(IExtensionContext context)
    {
        var models = new[]
        {
            new { Name = "gpt-4", MaxTokens = 8192, Capabilities = AIProviderCapability.Chat | AIProviderCapability.Streaming | AIProviderCapability.ToolCalling },
            new { Name = "gpt-3.5-turbo", MaxTokens = 4096, Capabilities = AIProviderCapability.Chat | AIProviderCapability.Streaming | AIProviderCapability.ToolCalling }
        };

        foreach (var model in models)
        {
            context.RegisterService<ModelInfo>(serviceProvider =>
            {
                return new ModelInfo
                {
                    ModelName = model.Name,
                    Provider = "OpenAI",
                    Capabilities = model.Capabilities,
                    MaxTokens = model.MaxTokens
                };
            });
        }
    }

    private AIProviderCapability GetModelCapabilities(string modelName)
    {
        return modelName.ToLower() switch
        {
            var name when name.StartsWith("gpt-4") => AIProviderCapability.Chat | AIProviderCapability.Streaming | AIProviderCapability.ToolCalling,
            var name when name.StartsWith("gpt-3.5") => AIProviderCapability.Chat | AIProviderCapability.Streaming | AIProviderCapability.ToolCalling,
            _ => AIProviderCapability.Chat
        };
    }

    private int GetModelMaxTokens(string modelName)
    {
        return modelName.ToLower() switch
        {
            var name when name.StartsWith("gpt-4") => 8192,
            var name when name.StartsWith("gpt-3.5") => 4096,
            _ => 2048
        };
    }

    private PricingInfo GetModelPricing(string modelName)
    {
        return modelName.ToLower() switch
        {
            "gpt-4" => new PricingInfo { InputTokenCost = 0.03m, OutputTokenCost = 0.06m },
            "gpt-3.5-turbo" => new PricingInfo { InputTokenCost = 0.0015m, OutputTokenCost = 0.002m },
            _ => new PricingInfo { InputTokenCost = 0.001m, OutputTokenCost = 0.001m }
        };
    }
}
```

### 2. 工具扩展

```csharp
// 工具扩展接口
public interface IToolExtension : IExtension
{
    IAevatarTool CreateTool(IServiceProvider serviceProvider);
    ToolDefinition GetToolDefinition();
    bool IsEnabled { get; }
    string ToolCategory { get; }
}

// 工具扩展基类
public abstract class ToolExtensionBase : IToolExtension
{
    protected readonly ILogger _logger;
    protected ToolDefinition _toolDefinition;
    protected ExtensionContext _context;

    protected ToolExtensionBase(ILogger logger)
    {
        _logger = logger;
    }

    public abstract string ExtensionId { get; }
    public abstract string ExtensionName { get; }
    public abstract string ExtensionDescription { get; }
    public abstract Version ExtensionVersion { get; }
    public abstract string ExtensionAuthor { get; }
    public abstract ExtensionPriority Priority { get; }
    public ExtensionState State { get; protected set; }

    public abstract IAevatarTool CreateTool(IServiceProvider serviceProvider);
    public abstract ToolDefinition GetToolDefinition();

    public virtual bool IsEnabled => true;
    public virtual string ToolCategory => "Custom";

    public virtual async Task InitializeAsync(IExtensionContext context, CancellationToken cancellationToken = default)
    {
        _context = context as ExtensionContext;
        State = ExtensionState.Initializing;

        _logger.LogInformation("Initializing tool extension {ExtensionName}", ExtensionName);

        try
        {
            // 初始化工具定义
            InitializeToolDefinition();

            // 注册工具
            RegisterTool(context);

            State = ExtensionState.Initialized;
            _logger.LogInformation("Tool extension {ExtensionName} initialized successfully", ExtensionName);
        }
        catch (Exception ex)
        {
            State = ExtensionState.Faulted;
            _logger.LogError(ex, "Failed to initialize tool extension {ExtensionName}", ExtensionName);
            throw;
        }
    }

    public virtual async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        State = ExtensionState.ShuttingDown;
        _logger.LogInformation("Shutting down tool extension {ExtensionName}", ExtensionName);

        // 清理资源
        await Task.CompletedTask;

        State = ExtensionState.Shutdown;
    }

    protected virtual void InitializeToolDefinition()
    {
        _toolDefinition = new ToolDefinition
        {
            Name = ExtensionName.Replace("Extension", "").Replace("Tool", ""),
            Description = ExtensionDescription,
            Category = ToolCategory,
            Version = ExtensionVersion.ToString(),
            Parameters = new List<ToolParameter>(),
            ReturnType = new ToolReturnType { Type = "object", Description = "Tool execution result" },
            RequiresConfirmation = false,
            RequiresAuthentication = false,
            Timeout = TimeSpan.FromSeconds(30),
            MaxRetryCount = 0
        };
    }

    protected virtual void RegisterTool(IExtensionContext context)
    {
        context.RegisterService<IAevatarTool>(serviceProvider => CreateTool(serviceProvider));
    }

    protected void DefineParameter(string name, string type, string description, bool required = true,
        object defaultValue = null, List<ValidationRule> validationRules = null)
    {
        var parameter = new ToolParameter
        {
            Name = name,
            Type = type,
            Description = description,
            Required = required,
            DefaultValue = defaultValue,
            ValidationRules = validationRules ?? new List<ValidationRule>()
        };

        _toolDefinition.Parameters.Add(parameter);
    }
}

// 具体的工具扩展实现
[Extension(
    id: "calculator-tool",
    name: "Calculator Tool Extension",
    description: "Provides mathematical calculation capabilities",
    version: "1.0.0",
    author: "Aevatar Team",
    priority = ExtensionPriority.Normal
)]
[ExtensionCapability("tool")]
[ExtensionCapability("calculator")]
public class CalculatorToolExtension : ToolExtensionBase
{
    public CalculatorToolExtension(ILogger<CalculatorToolExtension> logger) : base(logger)
    {
    }

    public override string ExtensionId => "calculator-tool";
    public override string ExtensionName => "Calculator Tool Extension";
    public override string ExtensionDescription => "Provides mathematical calculation capabilities";
    public override Version ExtensionVersion => new Version(1, 0, 0);
    public override string ExtensionAuthor => "Aevatar Team";
    public override ExtensionPriority Priority => ExtensionPriority.Normal;
    public override string ToolCategory => "Mathematics";

    protected override void InitializeToolDefinition()
    {
        base.InitializeToolDefinition();

        _toolDefinition.Name = "Calculator";
        _toolDefinition.Description = "Perform mathematical calculations";
        _toolDefinition.RequiresConfirmation = false;

        DefineParameter("operation", "string", "Mathematical operation (add, subtract, multiply, divide)", required: true,
            validationRules: new List<ValidationRule>
            {
                new ValidationRule
                {
                    Type = "regex",
                    Constraint = "^(add|subtract|multiply|divide)$",
                    ErrorMessage = "Operation must be one of: add, subtract, multiply, divide"
                }
            });

        DefineParameter("operands", "array", "Array of numbers to operate on", required: true,
            validationRules: new List<ValidationRule>
            {
                new ValidationRule
                {
                    Type = "custom",
                    CustomValidator = operands =>
                    {
                        if (operands is not List<object> list) return false;
                        return list.Count >= 2; // 至少需要两个操作数
                    },
                    ErrorMessage = "At least two operands are required"
                }
            });
    }

    public override IAevatarTool CreateTool(IServiceProvider serviceProvider)
    {
        return new CalculatorTool(_toolDefinition, _logger);
    }

    public override ToolDefinition GetToolDefinition()
    {
        return _toolDefinition;
    }

    private class CalculatorTool : AevatarToolBase
    {
        private readonly ToolDefinition _definition;

        public CalculatorTool(ToolDefinition definition, ILogger logger) : base(logger)
        {
            _definition = definition;
        }

        public override ToolDefinition Definition => _definition;

        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            try
            {
                var operation = parameters["operation"].ToString().ToLower();
                var operands = parameters["operands"] as List<object>;

                if (operands == null || operands.Count < 2)
                {
                    return ToolResult.Failure("At least two operands are required");
                }

                // 转换操作数为数字
                var numbers = operands.Select(Convert.ToDouble).ToList();

                double result = operation switch
                {
                    "add" => numbers.Sum(),
                    "subtract" => numbers.Skip(1).Aggregate(numbers[0], (a, b) => a - b),
                    "multiply" => numbers.Aggregate(1.0, (a, b) => a * b),
                    "divide" => numbers.Skip(1).Aggregate(numbers[0], (a, b) =>
                    {
                        if (b == 0) throw new DivideByZeroException("Division by zero");
                        return a / b;
                    }),
                    _ => throw new ArgumentException($"Unsupported operation: {operation}")
                };

                return ToolResult.Success(new
                {
                    operation,
                    operands = numbers,
                    result,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return ToolResult.Failure($"Calculation failed: {ex.Message}");
            }
        }
    }
}
```

### 3. 运行时扩展

```csharp
// 运行时扩展接口
public interface IRuntimeExtension : IExtension
{
    IAgentRuntime CreateRuntime(IServiceProvider serviceProvider);
    RuntimeCapabilities GetRuntimeCapabilities();
    bool IsSupported(RuntimeEnvironment environment);
    Task ConfigureRuntimeAsync(IAgentRuntime runtime, RuntimeConfiguration configuration);
}

// 自定义运行时扩展
[Extension(
    id: "custom-runtime",
    name: "Custom Runtime Extension",
    description: "Provides a custom agent runtime implementation",
    version: "1.0.0",
    author: "Aevatar Team",
    priority = ExtensionPriority.Normal
)]
[ExtensionCapability("runtime")]
[ExtensionCapability("custom")]
public class CustomRuntimeExtension : IRuntimeExtension
{
    private readonly ILogger<CustomRuntimeExtension> _logger;
    private ExtensionContext _context;

    public CustomRuntimeExtension(ILogger<CustomRuntimeExtension> logger)
    {
        _logger = logger;
    }

    public string ExtensionId => "custom-runtime";
    public string ExtensionName => "Custom Runtime Extension";
    public string ExtensionDescription => "Provides a custom agent runtime implementation";
    public Version ExtensionVersion => new Version(1, 0, 0);
    public string ExtensionAuthor => "Aevatar Team";
    public ExtensionPriority Priority => ExtensionPriority.Normal;
    public ExtensionState State { get; private set; }

    public async Task InitializeAsync(IExtensionContext context, CancellationToken cancellationToken = default)
    {
        _context = context as ExtensionContext;
        State = ExtensionState.Initializing;

        _logger.LogInformation("Initializing Custom Runtime Extension");

        try
        {
            // 注册运行时服务
            RegisterRuntimeServices(context);

            State = ExtensionState.Initialized;
            _logger.LogInformation("Custom Runtime Extension initialized successfully");
        }
        catch (Exception ex)
        {
            State = ExtensionState.Faulted;
            _logger.LogError(ex, "Failed to initialize Custom Runtime Extension");
            throw;
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        State = ExtensionState.ShuttingDown;
        _logger.LogInformation("Shutting down Custom Runtime Extension");

        // 清理资源
        await Task.CompletedTask;

        State = ExtensionState.Shutdown;
    }

    public IAgentRuntime CreateRuntime(IServiceProvider serviceProvider)
    {
        return serviceProvider.GetRequiredService<IAgentRuntime>();
    }

    public RuntimeCapabilities GetRuntimeCapabilities()
    {
        return new RuntimeCapabilities
        {
            SupportsPersistence = true,
            SupportsClustering = false,
            SupportsRemoting = true,
            SupportsLoadBalancing = false,
            SupportsFaultTolerance = true,
            SupportsScaling = false,
            MaxConcurrentAgents = 1000,
            MaxAgentLifetime = Timeout.InfiniteTimeSpan
        };
    }

    public bool IsSupported(RuntimeEnvironment environment)
    {
        return environment switch
        {
            RuntimeEnvironment.Development => true,
            RuntimeEnvironment.Testing => true,
            RuntimeEnvironment.Staging => true,
            RuntimeEnvironment.Production => false, // 自定义运行时不建议用于生产环境
            _ => false
        };
    }

    public async Task ConfigureRuntimeAsync(IAgentRuntime runtime, RuntimeConfiguration configuration)
    {
        _logger.LogInformation("Configuring custom runtime with configuration {ConfigurationId}", configuration.RuntimeId);

        // 应用自定义配置
        if (runtime is CustomAgentRuntime customRuntime)
        {
            customRuntime.Configure(configuration);
        }

        await Task.CompletedTask;
    }

    private void RegisterRuntimeServices(IExtensionContext context)
    {
        context.RegisterService<IAgentRuntime, CustomAgentRuntime>();
        context.RegisterService<ICustomRuntimeService, CustomRuntimeService>();
    }
}
```

## 🔧 扩展配置系统

### 1. 扩展配置模型

```csharp
public class ExtensionConfiguration
{
    public bool EnableAutoDiscovery { get; set; } = true;
    public List<string> ExtensionDirectories { get; set; } = new() { "./extensions", "./plugins" };
    public List<ExtensionInfo> Extensions { get; set; } = new();
    public Dictionary<string, object> ExtensionSettings { get; set; } = new();
    public ExtensionLoadingMode LoadingMode { get; set; } = ExtensionLoadingMode.Lazy;
    public bool EnableHotReload { get; set; } = false;
    public TimeSpan? HotReloadCheckInterval { get; set; } = TimeSpan.FromSeconds(30);
}

public enum ExtensionLoadingMode
{
    Eager,      // 启动时加载所有扩展
    Lazy,       // 按需加载扩展
    OnDemand    // 显式请求时加载
}

// 扩展设置
public class ExtensionSettings<TExtension> where TExtension : IExtension
{
    public bool IsEnabled { get; set; } = true;
    public Dictionary<string, object> Configuration { get; set; } = new();
    public List<string> Dependencies { get; set; } = new();
    public ExtensionPriority Priority { get; set; } = ExtensionPriority.Normal;
}
```

### 2. 扩展注册器

```csharp
public interface IExtensionRegistry
{
    Task RegisterExtensionAsync<TExtension>(ExtensionSettings<TExtension> settings = null) where TExtension : IExtension;
    Task RegisterExtensionAsync(IExtension extension, ExtensionSettings<IExtension> settings = null);
    Task UnregisterExtensionAsync(string extensionId);
    Task<IExtension> GetExtensionAsync(string extensionId);
    Task<List<IExtension>> GetExtensionsAsync();
    Task<List<TExtension>> GetExtensionsAsync<TExtension>() where TExtension : IExtension;
    Task<bool> ExtensionExistsAsync(string extensionId);
}

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAevatarExtensions(this IServiceCollection services, Action<ExtensionConfiguration> configure = null)
    {
        var configuration = new ExtensionConfiguration();
        configure?.Invoke(configuration);

        // 注册扩展服务
        services.AddSingleton<IExtensionDiscovery, ExtensionDiscovery>();
        services.AddSingleton<IExtensionLoader, ExtensionLoader>();
        services.AddSingleton<IExtensionRegistry, ExtensionRegistry>();
        services.AddSingleton<IExtensionManager, ExtensionManager>();

        // 注册扩展配置
        services.AddSingleton<IOptions<ExtensionConfiguration>>(provider => Options.Create(configuration));

        // 自动发现和注册扩展
        if (configuration.EnableAutoDiscovery)
        {
            services.AddHostedService<ExtensionDiscoveryService>();
        }

        return services;
    }
}
```

## 🔍 扩展生命周期管理

### 1. 生命周期事件

```csharp
public interface IExtensionLifecycle
{
    event EventHandler<ExtensionLoadingEventArgs> ExtensionLoading;
    event EventHandler<ExtensionLoadedEventArgs> ExtensionLoaded;
    event EventHandler<ExtensionUnloadingEventArgs> ExtensionUnloading;
    event EventHandler<ExtensionUnloadedEventArgs> ExtensionUnloaded;
    event EventHandler<ExtensionFaultedEventArgs> ExtensionFaulted;
}

public class ExtensionLifecycle : IExtensionLifecycle
{
    public event EventHandler<ExtensionLoadingEventArgs> ExtensionLoading;
    public event EventHandler<ExtensionLoadedEventArgs> ExtensionLoaded;
    public event EventHandler<ExtensionUnloadingEventArgs> ExtensionUnloading;
    public event EventHandler<ExtensionUnloadedEventArgs> ExtensionUnloaded;
    public event EventHandler<ExtensionFaultedEventArgs> ExtensionFaulted;

    public void OnExtensionLoading(ExtensionLoadingEventArgs args)
    {
        ExtensionLoading?.Invoke(this, args);
    }

    public void OnExtensionLoaded(ExtensionLoadedEventArgs args)
    {
        ExtensionLoaded?.Invoke(this, args);
    }

    public void OnExtensionUnloading(ExtensionUnloadingEventArgs args)
    {
        ExtensionUnloading?.Invoke(this, args);
    }

    public void OnExtensionUnloaded(ExtensionUnloadedEventArgs args)
    {
        ExtensionUnloaded?.Invoke(this, args);
    }

    public void OnExtensionFaulted(ExtensionFaultedEventArgs args)
    {
        ExtensionFaulted?.Invoke(this, args);
    }
}

public class ExtensionLoadingEventArgs : EventArgs
{
    public ExtensionInfo ExtensionInfo { get; }
    public DateTime LoadTime { get; }
    public CancellationToken CancellationToken { get; }

    public ExtensionLoadingEventArgs(ExtensionInfo extensionInfo, CancellationToken cancellationToken = default)
    {
        ExtensionInfo = extensionInfo;
        LoadTime = DateTime.UtcNow;
        CancellationToken = cancellationToken;
    }
}
```

### 2. 扩展管理器

```csharp
public interface IExtensionManager : IExtensionLifecycle
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task ShutdownAsync(CancellationToken cancellationToken = default);
    Task LoadAllExtensionsAsync(CancellationToken cancellationToken = default);
    Task UnloadAllExtensionsAsync(CancellationToken cancellationToken = default);
    Task ReloadExtensionAsync(string extensionId, CancellationToken cancellationToken = default);
    Task<ExtensionHealthCheckResult> HealthCheckAsync(string extensionId, CancellationToken cancellationToken = default);
}

public class ExtensionManager : IExtensionManager
{
    private readonly IExtensionDiscovery _discovery;
    private readonly IExtensionLoader _loader;
    private readonly IExtensionRegistry _registry;
    private readonly ExtensionConfiguration _configuration;
    private readonly ILogger<ExtensionManager> _logger;
    private readonly ExtensionLifecycle _lifecycle;

    public ExtensionManager(
        IExtensionDiscovery discovery,
        IExtensionLoader loader,
        IExtensionRegistry registry,
        IOptions<ExtensionConfiguration> configuration,
        ILogger<ExtensionManager> logger)
    {
        _discovery = discovery;
        _loader = loader;
        _registry = registry;
        _configuration = configuration.Value;
        _logger = logger;
        _lifecycle = new ExtensionLifecycle();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing ExtensionManager");

        try
        {
            // 发现扩展
            var extensions = await _discovery.DiscoverExtensionsAsync(cancellationToken);
            _logger.LogInformation("Discovered {ExtensionCount} extensions", extensions.Count);

            // 根据配置过滤扩展
            var enabledExtensions = extensions.Where(ext => _configuration.Extensions?.Any(config => config.ExtensionId == ext.ExtensionId && config.IsEnabled) != false);

            // 加载扩展
            if (_configuration.LoadingMode == ExtensionLoadingMode.Eager)
            {
                await LoadAllExtensionsAsync(cancellationToken);
            }

            _logger.LogInformation("ExtensionManager initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize ExtensionManager");
            throw;
        }
    }

    public async Task LoadAllExtensionsAsync(CancellationToken cancellationToken = default)
    {
        var extensions = await _discovery.DiscoverExtensionsAsync(cancellationToken);

        // 按优先级排序
        var sortedExtensions = extensions.OrderByDescending(ext => ext.Priority).ToList();

        foreach (var extensionInfo in sortedExtensions)
        {
            try
            {
                await LoadExtensionAsync(extensionInfo, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load extension {ExtensionId}", extensionInfo.ExtensionId);
            }
        }
    }

    private async Task LoadExtensionAsync(ExtensionInfo extensionInfo, CancellationToken cancellationToken)
    {
        if (!extensionInfo.IsEnabled)
        {
            _logger.LogInformation("Extension {ExtensionId} is disabled, skipping", extensionInfo.ExtensionId);
            return;
        }

        _lifecycle.OnExtensionLoading(new ExtensionLoadingEventArgs(extensionInfo, cancellationToken));

        try
        {
            // 创建扩展上下文
            var context = new ExtensionContext
            {
                Extension = null, // Will be set after loading
                ServiceProvider = _serviceProvider,
                Configuration = _configuration.ExtensionSettings.GetValueOrDefault(extensionInfo.ExtensionId) as IConfiguration,
                Logger = _logger
            };

            // 加载扩展
            var extension = await _loader.LoadExtensionAsync(extensionInfo, context, cancellationToken);

            // 注册扩展
            await _registry.RegisterExtensionAsync(extension);

            extensionInfo.State = ExtensionState.Loaded;
            extensionInfo.LoadTime = DateTime.UtcNow;

            _lifecycle.OnExtensionLoaded(new ExtensionLoadedEventArgs(extensionInfo, extension));

            _logger.LogInformation("Extension {ExtensionId} loaded successfully", extensionInfo.ExtensionId);
        }
        catch (Exception ex)
        {
            extensionInfo.State = ExtensionState.Faulted;
            extensionInfo.LoadError = ex;

            _lifecycle.OnExtensionFaulted(new ExtensionFaultedEventArgs(extensionInfo, ex));

            _logger.LogError(ex, "Failed to load extension {ExtensionId}", extensionInfo.ExtensionId);
            throw;
        }
    }

    public async Task ReloadExtensionAsync(string extensionId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Reloading extension {ExtensionId}", extensionId);

        // 卸载现有扩展
        if (await _registry.ExtensionExistsAsync(extensionId))
        {
            await _registry.UnregisterExtensionAsync(extensionId);
        }

        // 重新发现扩展
        var extensions = await _discovery.DiscoverExtensionsAsync(cancellationToken);
        var extensionInfo = extensions.FirstOrDefault(ext => ext.ExtensionId == extensionId);

        if (extensionInfo != null)
        {
            // 重新加载扩展
            await LoadExtensionAsync(extensionInfo, cancellationToken);
        }
        else
        {
            _logger.LogWarning("Extension {ExtensionId} not found for reloading", extensionId);
        }
    }

    public async Task<ExtensionHealthCheckResult> HealthCheckAsync(string extensionId, CancellationToken cancellationToken = default)
    {
        var extension = await _registry.GetExtensionAsync(extensionId);
        if (extension == null)
        {
            return new ExtensionHealthCheckResult
            {
                ExtensionId = extensionId,
                IsHealthy = false,
                Status = "Extension not found",
                LastCheckTime = DateTime.UtcNow
            };
        }

        try
        {
            // 执行健康检查
            var healthCheck = extension as IHealthCheck;
            if (healthCheck != null)
            {
                var healthResult = await healthCheck.CheckHealthAsync(cancellationToken);
                return new ExtensionHealthCheckResult
                {
                    ExtensionId = extensionId,
                    IsHealthy = healthResult.Status == HealthStatus.Healthy,
                    Status = healthResult.Description,
                    LastCheckTime = DateTime.UtcNow,
                    Details = healthResult.Data
                };
            }

            // 基本健康检查
            return new ExtensionHealthCheckResult
            {
                ExtensionId = extensionId,
                IsHealthy = extension.State == ExtensionState.Initialized,
                Status = extension.State.ToString(),
                LastCheckTime = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            return new ExtensionHealthCheckResult
            {
                ExtensionId = extensionId,
                IsHealthy = false,
                Status = $"Health check failed: {ex.Message}",
                LastCheckTime = DateTime.UtcNow,
                Exception = ex
            };
        }
    }

    // 事件转发
    public event EventHandler<ExtensionLoadingEventArgs> ExtensionLoading
    {
        add => _lifecycle.ExtensionLoading += value;
        remove => _lifecycle.ExtensionLoading -= value;
    }

    public event EventHandler<ExtensionLoadedEventArgs> ExtensionLoaded
    {
        add => _lifecycle.ExtensionLoaded += value;
        remove => _lifecycle.ExtensionLoaded -= value;
    }

    public event EventHandler<ExtensionUnloadingEventArgs> ExtensionUnloading
    {
        add => _lifecycle.ExtensionUnloading += value;
        remove => _lifecycle.ExtensionUnloading -= value;
    }

    public event EventHandler<ExtensionUnloadedEventArgs> ExtensionUnloaded
    {
        add => _lifecycle.ExtensionUnloaded += value;
        remove => _lifecycle.ExtensionUnloaded -= value;
    }

    public event EventHandler<ExtensionFaultedEventArgs> ExtensionFaulted
    {
        add => _lifecycle.ExtensionFaulted += value;
        remove => _lifecycle.ExtensionFaulted -= value;
    }
}
```

---

*本文档详细描述了框架的扩展性设计，包括扩展点定义、发现与加载机制、生命周期管理和具体的扩展实现，为开发和集成第三方扩展提供全面指导。*