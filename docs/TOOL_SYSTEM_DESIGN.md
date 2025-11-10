# Aevatar Agent Framework - 工具系统设计

 ## 🎯 工具系统概述

 工具系统是Aevatar Agent Framework的核心组件之一，它为AI代理提供了**安全、可扩展、类型安全**的外部能力调用机制。工具系统采用插件式架构，支持运行时动态发现和注册工具，同时提
 供完善的安全验证和访问控制机制。

 ## 🏗️ 工具系统架构

 ```
 ┌─────────────────────────────────────────────────────────┐
 │                工具使用层                                │
 │           AI代理调用工具                                  │
 ├─────────────────────────────────────────────────────────┤
 │                工具管理层                                │
 │  ┌───────────────────────────────────────────────────┐   │
 │  │  ToolManager  │  ToolRegistry  │  ToolDiscovery  │   │
 │  │  ToolValidator│  ToolExecutor   │  ToolContext    │   │
 │  └───────────────────────────────────────────────────┘   │
 ├─────────────────────────────────────────────────────────┤
 │                工具实现层                                │
 │  ┌──────────────┬──────────────┬────────────────────┐   │
 │  │  Core Tools  │Custom Tools  │  External Tools    │   │
 │  │(Built-in)    │(User Defined)│  (API Integration) │   │
 │  └──────────────┴──────────────┴────────────────────┘   │
 ├─────────────────────────────────────────────────────────┤
 │                安全验证层                                │
 │  ┌──────────────┬──────────────┬────────────────────┐   │
 │  │  Parameter   │  Access      │  Rate Limiting     │   │
 │  │  Validation  │  Control     │  & Throttling       │   │
 │  └──────────────┴──────────────┴────────────────────┘   │
 ├─────────────────────────────────────────────────────────┤
 │                执行引擎层                                │
 │  ┌──────────────┬──────────────┬────────────────────┐   │
 │  │  Sync Exec   │  Async Exec  │  Parallel Exec     │   │
 │  │  Pipeline    │  Retry Logic │  Circuit Breaker   │   │
 │  └──────────────┴──────────────┴────────────────────┘   │
 └─────────────────────────────────────────────────────────┘
 ```

 ## 🔧 核心接口设计

 ### 1. 基础工具接口

 ```csharp
 public interface IAevatarTool
 {
     // 工具标识
     string Name { get; }
     string Description { get; }
     string Category { get; }
     string Version { get; }

     // 工具定义
     ToolDefinition Definition { get; }

     // 执行方法
     Task<ToolResult> ExecuteAsync(
         Dictionary<string, object> parameters,
         CancellationToken cancellationToken = default);

     // 验证方法
     Task<ValidationResult> ValidateParametersAsync(Dictionary<string, object> parameters);
     Task<bool> HasPermissionAsync(IPrincipal principal, Dictionary<string, object> parameters);

     // 生命周期
     Task InitializeAsync(CancellationToken cancellationToken = default);
     Task CleanupAsync(CancellationToken cancellationToken = default);
 }

 // 工具定义详细信息
 public class ToolDefinition
 {
     public string Name { get; init; }
     public string Description { get; init; }
     public string Category { get; init; } = "General";
     public string Version { get; init; } = "1.0.0";

     public List<ToolParameter> Parameters { get; init; } = new();
     public ToolReturnType ReturnType { get; init; }

     // 安全设置
     public bool RequiresConfirmation { get; init; }
     public bool RequiresAuthentication { get; init; }
     public string[] RequiredRoles { get; init; } = Array.Empty<string>();
     public string[] RequiredPermissions { get; init; } = Array.Empty<string>();

     // 执行设置
     public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
     public int MaxRetryCount { get; init; } = 0;
     public bool AllowParallelExecution { get; init; } = true;

     // 元数据
     public Dictionary<string, object> Metadata { get; init; } = new();
     public Dictionary<string, string> Tags { get; init; } = new();
 }

 // 工具参数定义
 public class ToolParameter
 {
     public string Name { get; init; }
     public string Type { get; init; }
     public string Description { get; init; }
     public bool Required { get; init; }
     public object DefaultValue { get; init; }
     public List<ValidationRule> ValidationRules { get; init; } = new();
     public Dictionary<string, object> Constraints { get; init; } = new();
 }

 // 验证规则
 public class ValidationRule
 {
     public string Type { get; init; } // "range", "length", "regex", "custom"
     public object Constraint { get; init; }
     public string ErrorMessage { get; init; }
     public Func<object, bool> CustomValidator { get; init; }
 }

 // 工具执行结果
 public class ToolResult
 {
     public bool Success { get; init; }
     public object Data { get; init; }
     public string ErrorMessage { get; init; }
     public List<ValidationError> ValidationErrors { get; init; } = new();
     public TimeSpan ExecutionTime { get; init; }
     public Dictionary<string, object> Metadata { get; init; } = new();

     public static ToolResult Success(object data = null) =>
         new() { Success = true, Data = data };

     public static ToolResult Failure(string errorMessage, List<ValidationError> errors = null) =>
         new() { Success = false, ErrorMessage = errorMessage, ValidationErrors = errors ?? new() };
 }
 ```

 ### 2. 工具上下文

 ```csharp
 public interface IToolContext
 {
     // 执行上下文
     string AgentId { get; }
     IServiceProvider ServiceProvider { get; }
     CancellationToken CancellationToken { get; }
     IPrincipal Principal { get; }

     // 状态访问
     TState GetAgentState<TState>() where TState : class;
     Task<TState> GetAgentStateAsync<TState>(string agentId) where TState : class;

     // 事件访问
     Task PublishEventAsync<TEvent>(TEvent @event, EventDirection direction = EventDirection.Bidirectional);
     Task<List<TEvent>> GetRecentEventsAsync<TEvent>(int count = 10);

     // 内存访问
     Task<List<MemoryItem>> SearchMemoryAsync(string query, int maxResults = 10);
     Task StoreInMemoryAsync(MemoryItem item);

     // 配置访问
     TConfig GetConfiguration<TConfig>() where TConfig : class, new();
     object GetConfiguration(string key);

     // 日志记录
     void LogInformation(string message, params object[] args);
     void LogWarning(string message, params object[] args);
     void LogError(Exception exception, string message, params object[] args);
 }

 public class ToolContext : IToolContext
 {
     private readonly IAgentRepository _agentRepository;
     private readonly IEventPublisher _eventPublisher;
     private readonly IAevatarMemory _memory;
     private readonly IConfiguration _configuration;
     private readonly ILogger _logger;

     public ToolContext(
         string agentId,
         IServiceProvider serviceProvider,
         IPrincipal principal,
         CancellationToken cancellationToken)
     {
         AgentId = agentId;
         ServiceProvider = serviceProvider;
         Principal = principal;
         CancellationToken = cancellationToken;

         // 解析依赖
         _agentRepository = serviceProvider.GetRequiredService<IAgentRepository>();
         _eventPublisher = serviceProvider.GetRequiredService<IEventPublisher>();
         _memory = serviceProvider.GetRequiredService<IAevatarMemory>();
         _configuration = serviceProvider.GetService<IConfiguration>();
         _logger = serviceProvider.GetRequiredService<ILogger<ToolContext>>();
     }

     public TState GetAgentState<TState>() where TState : class
     {
         return _agentRepository.GetAgentState<TState>(AgentId);
     }

     public async Task<TState> GetAgentStateAsync<TState>(string agentId) where TState : class
     {
         return await _agentRepository.GetAgentStateAsync<TState>(agentId);
     }

     public async Task PublishEventAsync<TEvent>(TEvent @event, EventDirection direction = EventDirection.Bidirectional)
     {
         await _eventPublisher.PublishAsync(@event, direction);
     }

     public async Task<List<TEvent>> GetRecentEventsAsync<TEvent>(int count = 10)
     {
         return await _eventPublisher.GetRecentEventsAsync<TEvent>(AgentId, count);
     }

     public async Task<List<MemoryItem>> SearchMemoryAsync(string query, int maxResults = 10)
     {
         return await _memory.SearchLongTermMemoryAsync(AgentId, query, maxResults);
     }

     public async Task StoreInMemoryAsync(MemoryItem item)
     {
         await _memory.StoreInLongTermMemoryAsync(AgentId, item);
     }

     public TConfig GetConfiguration<TConfig>() where TConfig : class, new()
     {
         return _configuration?.Get<TConfig>() ?? new TConfig();
     }

     public object GetConfiguration(string key)
     {
         return _configuration?[key];
     }

     public void LogInformation(string message, params object[] args)
     {
         _logger.LogInformation($"[{AgentId}] {message}", args);
     }

     public void LogWarning(string message, params object[] args)
     {
         _logger.LogWarning($"[{AgentId}] {message}", args);
     }

     public void LogError(Exception exception, string message, params object[] args)
     {
         _logger.LogError(exception, $"[{AgentId}] {message}", args);
     }
 }
 ```

 ### 3. 工具管理器

 ```csharp
 public interface IAevatarToolManager
 {
     // 工具注册
     Task RegisterToolAsync(IAevatarTool tool);
     Task RegisterToolsAsync(IEnumerable<IAevatarTool> tools);
     Task UnregisterToolAsync(string toolName);
     Task UnregisterToolsAsync(IEnumerable<string> toolNames);

     // 工具查询
     Task<IAevatarTool> GetToolAsync(string toolName);
     Task<List<IAevatarTool>> GetAllToolsAsync();
     Task<List<IAevatarTool>> GetToolsByCategoryAsync(string category);
     Task<List<IAevatarTool>> GetToolsByTagAsync(string tag);
     Task<bool> ToolExistsAsync(string toolName);

     // 工具发现
     Task AutoDiscoverToolsAsync(Assembly assembly = null);
     Task RegisterToolsFromAgentAsync<TAgent>() where TAgent : IAIGAgent;
     Task RegisterToolsFromConfigurationAsync(IConfiguration configuration);

     // 工具执行
     Task<ToolResult> ExecuteToolAsync(
         string toolName,
         Dictionary<string, object> parameters,
         IToolContext context = null,
         CancellationToken cancellationToken = default);

     Task<List<ToolResult>> ExecuteToolsAsync(
         List<ToolExecutionRequest> requests,
         IToolContext context = null,
         CancellationToken cancellationToken = default);

     // 工具验证
     Task<ValidationResult> ValidateToolAsync(string toolName, Dictionary<string, object> parameters);
     Task<bool> HasPermissionAsync(string toolName, IPrincipal principal, Dictionary<string, object> parameters);

     // 事件和通知
     event EventHandler<ToolRegisteredEventArgs> ToolRegistered;
     event EventHandler<ToolUnregisteredEventArgs> ToolUnregistered;
     event EventHandler<ToolExecutedEventArgs> ToolExecuted;
 }

 public class AevatarToolManager : IAevatarToolManager
 {
     private readonly ConcurrentDictionary<string, IAevatarTool> _tools;
     private readonly IToolValidator _validator;
     private readonly IToolExecutor _executor;
     private readonly IToolContextFactory _contextFactory;
     private readonly ILogger<AevatarToolManager> _logger;

     public async Task RegisterToolAsync(IAevatarTool tool)
     {
         if (tool == null)
             throw new ArgumentNullException(nameof(tool));

         // 验证工具定义
         var validationResult = await _validator.ValidateToolDefinitionAsync(tool.Definition);
         if (!validationResult.IsValid)
         {
             throw new InvalidToolException($"Tool {tool.Name} validation failed: {validationResult.ErrorMessage}");
         }

         // 初始化工具
         await tool.InitializeAsync();

         // 注册工具
         _tools[tool.Name] = tool;

         _logger.LogInformation("Tool {ToolName} registered successfully", tool.Name);
         ToolRegistered?.Invoke(this, new ToolRegisteredEventArgs(tool));
     }

     public async Task<ToolResult> ExecuteToolAsync(
         string toolName,
         Dictionary<string, object> parameters,
         IToolContext context = null,
         CancellationToken cancellationToken = default)
     {
         if (!_tools.TryGetValue(toolName, out var tool))
         {
             return ToolResult.Failure($"Tool '{toolName}' not found");
         }

         context ??= await _contextFactory.CreateContextAsync(cancellationToken);

         try
         {
             _logger.LogInformation("Executing tool {ToolName} for agent {AgentId}", toolName, context?.AgentId);

             // 1. 参数验证
             var validationResult = await tool.ValidateParametersAsync(parameters);
             if (!validationResult.IsValid)
             {
                 return ToolResult.Failure("Parameter validation failed", validationResult.Errors);
             }

             // 2. 权限检查
             if (context?.Principal != null)
             {
                 var hasPermission = await tool.HasPermissionAsync(context.Principal, parameters);
                 if (!hasPermission)
                 {
                     return ToolResult.Failure($"Insufficient permissions to execute tool '{toolName}'");
                 }
             }

             // 3. 执行工具
             var result = await _executor.ExecuteAsync(tool, parameters, context, cancellationToken);

             // 4. 记录执行
             ToolExecuted?.Invoke(this, new ToolExecutedEventArgs(tool, result));

             _logger.LogInformation("Tool {ToolName} executed successfully in {ExecutionTime}ms",
                 toolName, result.ExecutionTime.TotalMilliseconds);

             return result;
         }
         catch (Exception ex)
         {
             _logger.LogError(ex, "Tool {ToolName} execution failed", toolName);
             return ToolResult.Failure($"Tool execution failed: {ex.Message}");
         }
     }

     public async Task AutoDiscoverToolsAsync(Assembly assembly = null)
     {
         assembly ??= Assembly.GetExecutingAssembly();

         var toolTypes = assembly.GetTypes()
             .Where(t => typeof(IAevatarTool).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
             .ToList();

         foreach (var toolType in toolTypes)
         {
             try
             {
                 var tool = ActivatorUtilities.CreateInstance(_serviceProvider, toolType) as IAevatarTool;
                 await RegisterToolAsync(tool);
             }
             catch (Exception ex)
             {
                 _logger.LogWarning(ex, "Failed to auto-register tool {ToolType}", toolType.Name);
             }
         }
     }
 }
 ```

 ## 🛠️ 工具基类实现

 ### 1. 基础工具基类

 ```csharp
 public abstract class AevatarToolBase : IAevatarTool
 {
     protected ToolDefinition _definition;
     protected readonly ILogger _logger;

     protected AevatarToolBase(ILogger logger = null)
     {
         _logger = logger ?? NullLogger.Instance;
         InitializeDefinition();
     }

     protected virtual void InitializeDefinition()
     {
         var toolAttribute = GetType().GetCustomAttribute<ToolAttribute>();
         _definition = new ToolDefinition
         {
             Name = toolAttribute?.Name ?? GetType().Name.Replace("Tool", ""),
             Description = toolAttribute?.Description ?? "No description provided",
             Category = toolAttribute?.Category ?? "General",
             Version = toolAttribute?.Version ?? "1.0.0",
             Parameters = new List<ToolParameter>(),
             ReturnType = new ToolReturnType { Type = "object", Description = "Tool execution result" }
         };
     }

     protected void DefineParameter(
         string name,
         string type,
         string description,
         bool required = true,
         object defaultValue = null,
         List<ValidationRule> validationRules = null)
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

         _definition.Parameters.Add(parameter);
     }

     protected void AddValidationRule(string parameterName, ValidationRule rule)
     {
         var parameter = _definition.Parameters.FirstOrDefault(p => p.Name == parameterName);
         if (parameter != null)
         {
             parameter.ValidationRules.Add(rule);
         }
     }

     public string Name => _definition.Name;
     public string Description => _definition.Description;
     public string Category => _definition.Category;
     public string Version => _definition.Version;
     public ToolDefinition Definition => _definition;

     public virtual async Task InitializeAsync(CancellationToken cancellationToken = default)
     {
         _logger.LogInformation("Initializing tool {ToolName}", Name);
         await Task.CompletedTask;
     }

     public virtual async Task CleanupAsync(CancellationToken cancellationToken = default)
     {
         _logger.LogInformation("Cleaning up tool {ToolName}", Name);
         await Task.CompletedTask;
     }

     public abstract Task<ToolResult> ExecuteAsync(
         Dictionary<string, object> parameters,
         CancellationToken cancellationToken = default);

     public virtual async Task<ValidationResult> ValidateParametersAsync(Dictionary<string, object> parameters)
     {
         var errors = new List<ValidationError>();

         foreach (var parameter in _definition.Parameters)
         {
             var hasValue = parameters.TryGetValue(parameter.Name, out var value);

             // 检查必需参数
             if (parameter.Required && !hasValue)
             {
                 errors.Add(new ValidationError(parameter.Name, $"Parameter '{parameter.Name}' is required"));
                 continue;
             }

             // 使用默认值
             if (!hasValue && parameter.DefaultValue != null)
             {
                 parameters[parameter.Name] = parameter.DefaultValue;
                 value = parameter.DefaultValue;
             }

             // 跳过空值
             if (!hasValue) continue;

             // 验证参数类型
             var typeValidation = ValidateParameterType(parameter, value);
             if (!typeValidation.IsValid)
             {
                 errors.AddRange(typeValidation.Errors);
                 continue;
             }

             // 验证规则
             foreach (var rule in parameter.ValidationRules)
             {
                 var ruleValidation = await ValidateRuleAsync(parameter.Name, value, rule);
                 if (!ruleValidation.IsValid)
                 {
                     errors.AddRange(ruleValidation.Errors);
                 }
             }
         }

         return new ValidationResult(errors.Count == 0, errors);
     }

     public virtual async Task<bool> HasPermissionAsync(IPrincipal principal, Dictionary<string, object> parameters)
     {
         // 检查角色
         if (_definition.RequiredRoles?.Any() == true)
         {
             foreach (var requiredRole in _definition.RequiredRoles)
             {
                 if (!principal.IsInRole(requiredRole))
                 {
                     return false;
                 }
             }
         }

         // 检查权限
         if (_definition.RequiredPermissions?.Any() == true)
         {
             foreach (var requiredPermission in _definition.RequiredPermissions)
             {
                 if (!await CheckPermissionAsync(principal, requiredPermission, parameters))
                 {
                     return false;
                 }
             }
         }

         return true;
     }

     protected virtual async Task<bool> CheckPermissionAsync(IPrincipal principal, string permission, Dictionary<string, object> parameters)
     {
         // 子类可以重写以提供特定的权限检查逻辑
         return true;
     }

     private ValidationResult ValidateParameterType(ToolParameter parameter, object value)
     {
         var errors = new List<ValidationError>();

         try
         {
             var targetType = Type.GetType(parameter.Type) ?? typeof(object);
             var convertedValue = Convert.ChangeType(value, targetType);

             // 类型转换成功，更新参数值
             if (!ReferenceEquals(convertedValue, value))
             {
                 // 注意：这里需要在调用者中更新参数值
             }
         }
         catch (Exception ex)
         {
             errors.Add(new ValidationError(parameter.Name, $"Parameter '{parameter.Name}' must be of type {parameter.Type}: {ex.Message}"));
         }

         return new ValidationResult(errors.Count == 0, errors);
     }

     private async Task<ValidationResult> ValidateRuleAsync(string parameterName, object value, ValidationRule rule)
     {
         var errors = new List<ValidationError>();

         switch (rule.Type.ToLower())
         {
             case "range":
                 if (!IsInRange(value, rule.Constraint))
                 {
                     errors.Add(new ValidationError(parameterName, rule.ErrorMessage ?? $"Parameter '{parameterName}' is out of range"));
                 }
                 break;

             case "length":
                 if (!HasValidLength(value, rule.Constraint))
                 {
                     errors.Add(new ValidationError(parameterName, rule.ErrorMessage ?? $"Parameter '{parameterName}' has invalid length"));
                 }
                 break;

             case "regex":
                 if (!MatchesPattern(value, rule.Constraint.ToString()))
                 {
                     errors.Add(new ValidationError(parameterName, rule.ErrorMessage ?? $"Parameter '{parameterName}' does not match required pattern"));
                 }
                 break;

             case "custom":
                 if (rule.CustomValidator != null && !rule.CustomValidator(value))
                 {
                     errors.Add(new ValidationError(parameterName, rule.ErrorMessage ?? $"Parameter '{parameterName}' failed custom validation"));
                 }
                 break;
         }

         await Task.CompletedTask;
         return new ValidationResult(errors.Count == 0, errors);
     }

     private bool IsInRange(object value, object constraint)
     {
         // 实现范围验证逻辑
         return true;
     }

     private bool HasValidLength(object value, object constraint)
     {
         // 实现长度验证逻辑
         return true;
     }

     private bool MatchesPattern(object value, string pattern)
     {
         if (value is string stringValue)
         {
             return Regex.IsMatch(stringValue, pattern);
         }
         return false;
     }
 }
 ```

 ### 2. 工具属性定义

 ```csharp
 [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
 public class ToolAttribute : Attribute
 {
     public string Name { get; }
     public string Description { get; }
     public string Category { get; set; } = "General";
     public string Version { get; set; } = "1.0.0";
     public bool RequiresConfirmation { get; set; } = false;
     public bool RequiresAuthentication { get; set; } = false;
     public string[] RequiredRoles { get; set; } = Array.Empty<string>();
     public string[] RequiredPermissions { get; set; } = Array.Empty<string>();
     public int TimeoutSeconds { get; set; } = 30;
     public int MaxRetryCount { get; set; } = 0;
     public bool AllowParallelExecution { get; set; } = true;

     public ToolAttribute(string name, string description)
     {
         Name = name ?? throw new ArgumentNullException(nameof(name));
         Description = description ?? throw new ArgumentNullException(nameof(description));
     }
 }

 [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
 public class ToolParameterAttribute : Attribute
 {
     public string Name { get; }
     public string Type { get; }
     public string Description { get; }
     public bool Required { get; set; } = true;
     public object DefaultValue { get; set; }

     public ToolParameterAttribute(string name, string type, string description)
     {
         Name = name ?? throw new ArgumentNullException(nameof(name));
         Type = type ?? throw new ArgumentNullException(nameof(type));
         Description = description ?? throw new ArgumentNullException(nameof(description));
     }
 }
 ```

 ## 🛡️ 安全与验证

 ### 1. 工具验证器

 ```csharp
 public interface IToolValidator
 {
     Task<ValidationResult> ValidateToolDefinitionAsync(ToolDefinition definition);
     Task<ValidationResult> ValidateParametersAsync(ToolDefinition definition, Dictionary<string, object> parameters);
     Task<ValidationResult> ValidatePermissionsAsync(IPrincipal principal, ToolDefinition definition, Dictionary<string, object> parameters);
 }

 public class ToolValidator : IToolValidator
 {
     private readonly List<IToolValidationRule> _validationRules;

     public ToolValidator()
     {
         _validationRules = new List<IToolValidationRule>
         {
             new ToolNameValidationRule(),
             new ToolParameterValidationRule(),
             new ToolSecurityValidationRule(),
             new ToolTimeoutValidationRule()
         };
     }

     public async Task<ValidationResult> ValidateToolDefinitionAsync(ToolDefinition definition)
     {
         var errors = new List<ValidationError>();

         foreach (var rule in _validationRules)
         {
             var result = await rule.ValidateAsync(definition);
             if (!result.IsValid)
             {
                 errors.AddRange(result.Errors);
             }
         }

         return new ValidationResult(errors.Count == 0, errors);
     }

     public async Task<ValidationResult> ValidateParametersAsync(ToolDefinition definition, Dictionary<string, object> parameters)
     {
         var errors = new List<ValidationError>();

         // 验证必需参数
         var requiredParameters = definition.Parameters.Where(p => p.Required).Select(p => p.Name).ToList();
         var missingParameters = requiredParameters.Except(parameters.Keys).ToList();

         if (missingParameters.Any())
         {
             errors.AddRange(missingParameters.Select(param =>
                 new ValidationError(param, $"Required parameter '{param}' is missing")));
         }

         // 验证参数类型和约束
         foreach (var parameter in definition.Parameters)
         {
             if (parameters.TryGetValue(parameter.Name, out var value))
             {
                 var paramErrors = await ValidateParameterValueAsync(parameter, value);
                 errors.AddRange(paramErrors);
             }
         }

         return new ValidationResult(errors.Count == 0, errors);
     }

     private async Task<List<ValidationError>> ValidateParameterValueAsync(ToolParameter parameter, object value)
     {
         var errors = new List<ValidationError>();

         // 类型验证
         if (!IsValidType(parameter.Type, value))
         {
             errors.Add(new ValidationError(parameter.Name, $"Parameter '{parameter.Name}' must be of type {parameter.Type}"));
             return errors;
         }

         // 约束验证
         foreach (var constraint in parameter.Constraints)
         {
             var constraintErrors = await ValidateConstraintAsync(parameter, value, constraint);
             errors.AddRange(constraintErrors);
         }

         // 验证规则
         foreach (var rule in parameter.ValidationRules)
         {
             var ruleErrors = await ValidateRuleAsync(parameter, value, rule);
             errors.AddRange(ruleErrors);
         }

         await Task.CompletedTask;
         return errors;
     }
 }
 ```

 ### 2. 访问控制

 ```csharp
 public interface IToolAccessControl
 {
     Task<bool> CanExecuteToolAsync(IPrincipal principal, string toolName, Dictionary<string, object> parameters);
     Task<List<string>> GetAllowedToolsAsync(IPrincipal principal);
     Task<List<string>> GetDeniedToolsAsync(IPrincipal principal);
     Task GrantToolAccessAsync(IPrincipal principal, string toolName);
     Task RevokeToolAccessAsync(IPrincipal principal, string toolName);
 }

 public class ToolAccessControl : IToolAccessControl
 {
     private readonly IToolPermissionStore _permissionStore;
     private readonly IToolAuditLogger _auditLogger;

     public async Task<bool> CanExecuteToolAsync(IPrincipal principal, string toolName, Dictionary<string, object> parameters)
     {
         var userId = principal.Identity?.Name;
         if (string.IsNullOrEmpty(userId))
         {
             return false;
         }

         // 检查全局权限
         var hasGlobalPermission = await _permissionStore.HasGlobalPermissionAsync(userId, toolName);
         if (hasGlobalPermission)
         {
             await _auditLogger.LogAccessGrantedAsync(userId, toolName, parameters);
             return true;
         }

         // 检查参数特定权限
         var hasParameterPermission = await _permissionStore.HasParameterPermissionAsync(userId, toolName, parameters);
         if (hasParameterPermission)
         {
             await _auditLogger.LogAccessGrantedAsync(userId, toolName, parameters);
             return true;
         }

         await _auditLogger.LogAccessDeniedAsync(userId, toolName, parameters, "Insufficient permissions");
         return false;
     }
 }
 ```

 ## ⚡ 执行引擎

 ### 1. 工具执行器

 ```csharp
 public interface IToolExecutor
 {
     Task<ToolResult> ExecuteAsync(
         IAevatarTool tool,
         Dictionary<string, object> parameters,
         IToolContext context = null,
         CancellationToken cancellationToken = default);

     Task<List<ToolResult>> ExecuteBatchAsync(
         List<ToolExecutionRequest> requests,
         IToolContext context = null,
         CancellationToken cancellationToken = default);
 }

 public class ToolExecutor : IToolExecutor
 {
     private readonly IToolRetryPolicy _retryPolicy;
     private readonly IToolCircuitBreaker _circuitBreaker;
     private readonly IToolRateLimiter _rateLimiter;
     private readonly IToolTimeoutManager _timeoutManager;

     public async Task<ToolResult> ExecuteAsync(
         IAevatarTool tool,
         Dictionary<string, object> parameters,
         IToolContext context = null,
         CancellationToken cancellationToken = default)
     {
         var stopwatch = Stopwatch.StartNew();

         try
         {
             // 1. 速率限制检查
             await _rateLimiter.CheckRateLimitAsync(tool.Name);

             // 2. 熔断器检查
             if (!_circuitBreaker.CanExecute(tool.Name))
             {
                 return ToolResult.Failure($"Tool '{tool.Name}' is currently unavailable due to circuit breaker");
             }

             // 3. 创建执行上下文
             var executionContext = CreateExecutionContext(tool, parameters, context);

             // 4. 设置超时
             using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
             var timeout = tool.Definition.Timeout;
             timeoutCts.CancelAfter(timeout);

             // 5. 执行工具（带重试）
             var result = await ExecuteWithRetryAsync(tool, parameters, executionContext, timeoutCts.Token);

             stopwatch.Stop();

             // 6. 记录成功执行
             await _circuitBreaker.RecordSuccessAsync(tool.Name);

             // 7. 更新执行时间
             result = new ToolResult
             {
                 Success = result.Success,
                 Data = result.Data,
                 ErrorMessage = result.ErrorMessage,
                 ValidationErrors = result.ValidationErrors,
                 ExecutionTime = stopwatch.Elapsed,
                 Metadata = result.Metadata
             };

             return result;
         }
         catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
         {
             return ToolResult.Failure($"Tool '{tool.Name}' execution was cancelled");
         }
         catch (OperationCanceledException)
         {
             return ToolResult.Failure($"Tool '{tool.Name}' execution timed out after {tool.Definition.Timeout.TotalSeconds} seconds");
         }
         catch (Exception ex)
         {
             stopwatch.Stop();

             // 记录失败
             await _circuitBreaker.RecordFailureAsync(tool.Name);

             _logger.LogError(ex, "Tool '{ToolName}' execution failed", tool.Name);
             return ToolResult.Failure($"Tool execution failed: {ex.Message}");
         }
     }

     private async Task<ToolResult> ExecuteWithRetryAsync(
         IAevatarTool tool,
         Dictionary<string, object> parameters,
         IToolContext context,
         CancellationToken cancellationToken)
     {
         var retryPolicy = _retryPolicy.GetRetryPolicy(tool.Definition.MaxRetryCount);

         return await retryPolicy.ExecuteAsync(async () =>
         {
             return await tool.ExecuteAsync(parameters, cancellationToken);
         });
     }
 }
 ```

 ### 2. 批处理执行

 ```csharp
 public class BatchToolExecutor : IToolExecutor
 {
     private readonly IToolExecutor _individualExecutor;
     private readonly IBatchExecutionStrategy _batchStrategy;

     public async Task<List<ToolResult>> ExecuteBatchAsync(
         List<ToolExecutionRequest> requests,
         IToolContext context = null,
         CancellationToken cancellationToken = default)
     {
         // 分组请求以优化执行
         var executionGroups = _batchStrategy.GroupRequests(requests);

         var results = new List<ToolResult>();

         // 并行执行组
         var tasks = executionGroups.Select(async group =>
         {
             if (group.CanExecuteInParallel)
             {
                 // 并行执行
                 var parallelResults = await ExecuteParallelAsync(group, context, cancellationToken);
                 return parallelResults;
             }
             else
             {
                 // 顺序执行
                 var sequentialResults = await ExecuteSequentialAsync(group, context, cancellationToken);
                 return sequentialResults;
             }
         });

         var groupResults = await Task.WhenAll(tasks);

         // 展平结果并保持原始顺序
         foreach (var groupResult in groupResults)
         {
             results.AddRange(groupResult);
         }

         return results;
     }

     private async Task<List<ToolResult>> ExecuteParallelAsync(
         ExecutionGroup group,
         IToolContext context,
         CancellationToken cancellationToken)
     {
         var results = new ConcurrentBag<(ToolResult, int)>();

         var parallelOptions = new ParallelOptions
         {
             MaxDegreeOfParallelism = group.MaxParallelism,
             CancellationToken = cancellationToken
         };

         await Parallel.ForEachAsync(group.Requests, parallelOptions, async (request, ct) =>
         {
             var result = await _individualExecutor.ExecuteAsync(
                 request.Tool,
                 request.Parameters,
                 context,
                 ct);

             results.Add((result, request.Index));
         });

         // 按原始索引排序
         return results.OrderBy(x => x.Item2).Select(x => x.Item1).ToList();
     }
 }
 ```

 ## 📊 工具监控与度量

 ### 1. 工具使用指标

 ```csharp
 public interface IToolMetricsCollector
 {
     void RecordToolExecution(string toolName, TimeSpan duration, bool success);
     void RecordToolError(string toolName, string errorType);
     void RecordToolTimeout(string toolName);
     void RecordToolRetry(string toolName, int retryCount);
     void RecordToolRateLimitHit(string toolName);
     void RecordToolCircuitBreakerOpen(string toolName);
 }

 public class ToolMetricsCollector : IToolMetricsCollector
 {
     private readonly IMetrics _metrics;

     public void RecordToolExecution(string toolName, TimeSpan duration, bool success)
     {
         var tags = new Dictionary<string, string>
         {
             ["tool_name"] = toolName,
             ["success"] = success.ToString()
         };

         _metrics.IncrementCounter("tools.executions", tags);
         _metrics.RecordHistogram("tools.execution_duration_ms", duration.TotalMilliseconds, tags);
     }

     public void RecordToolError(string toolName, string errorType)
     {
         var tags = new Dictionary<string, string>
         {
             ["tool_name"] = toolName,
             ["error_type"] = errorType
         };

         _metrics.IncrementCounter("tools.errors", tags);
     }
 }
 ```

 ### 2. 工具审计日志

 ```csharp
 public interface IToolAuditLogger
 {
     Task LogToolExecutionAsync(string toolName, Dictionary<string, object> parameters, ToolResult result, IPrincipal principal);
     Task LogToolAccessDeniedAsync(string toolName, Dictionary<string, object> parameters, IPrincipal principal, string reason);
     Task LogToolValidationFailedAsync(string toolName, Dictionary<string, object> parameters, List<ValidationError> errors);
     Task LogToolRateLimitedAsync(string toolName, IPrincipal principal);
 }

 public class ToolAuditLogger : IToolAuditLogger
 {
     private readonly ILogger<ToolAuditLogger> _logger;
     private readonly IToolAuditStore _auditStore;

     public async Task LogToolExecutionAsync(string toolName, Dictionary<string, object> parameters, ToolResult result, IPrincipal principal)
     {
         var auditEntry = new ToolAuditEntry
         {
             Timestamp = DateTime.UtcNow,
             ToolName = toolName,
             UserId = principal.Identity?.Name ?? "anonymous",
             Parameters = SanitizeParameters(parameters),
             Success = result.Success,
             ErrorMessage = result.ErrorMessage,
             ExecutionTime = result.ExecutionTime
         };

         // 存储审计记录
         await _auditStore.StoreAsync(auditEntry);

         // 记录日志
         _logger.LogInformation("Tool execution: {ToolName} by {UserId} - Success: {Success}, Duration: {Duration}ms",
             toolName, auditEntry.UserId, result.Success, result.ExecutionTime.TotalMilliseconds);
     }

     private Dictionary<string, object> SanitizeParameters(Dictionary<string, object> parameters)
     {
         // 移除敏感信息
         var sanitized = new Dictionary<string, object>();
         foreach (var param in parameters)
         {
             if (IsSensitiveParameter(param.Key))
             {
                 sanitized[param.Key] = "***REDACTED***";
             }
             else
             {
                 sanitized[param.Key] = param.Value;
             }
         }
         return sanitized;
     }

     private bool IsSensitiveParameter(string parameterName)
     {
         var sensitivePatterns = new[] { "password", "token", "secret", "key", "auth" };
         return sensitivePatterns.Any(pattern => parameterName.ToLower().Contains(pattern));
     }
 }
 ```

 ## 🔧 工具开发最佳实践

 ### 1. 工具实现模板

 ```csharp
 [Tool("DataValidator", "Validates data against specified rules")]
 public class DataValidatorTool : AevatarToolBase
 {
     public DataValidatorTool(ILogger<DataValidatorTool> logger) : base(logger)
     {
         // ✅ 好的工具设计

         // 1. 清晰的工具定义
         DefineParameter("data", "object", "Data to validate", required: true);
         DefineParameter("rules", "array", "Validation rules to apply", required: true);
         DefineParameter("strict", "boolean", "Enable strict validation", required: false, defaultValue: false);

         // 2. 完善的参数验证
         AddValidationRule("rules", new ValidationRule
         {
             Type = "custom",
             CustomValidator = rules =>
             {
                 if (rules is not List<object> ruleList) return false;
                 return ruleList.All(rule => rule is Dictionary<string, object>);
             },
             ErrorMessage = "Rules must be an array of rule objects"
         });

         // 3. 合理的超时设置
         _definition.Timeout = TimeSpan.FromSeconds(10);
         _definition.MaxRetryCount = 0; // 验证操作不重试
         _definition.RequiresConfirmation = false; // 安全操作不需要确认
     }

     public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
     {
         try
         {
             var data = parameters["data"];
             var rules = parameters["rules"] as List<object>;
             var strict = Convert.ToBoolean(parameters.GetValueOrDefault("strict", false));

             // 4. 详细的执行逻辑
             var validationResults = new List<ValidationResult>();
             var errors = new List<string>();

             foreach (var ruleObj in rules)
             {
                 if (ruleObj is Dictionary<string, object> rule)
                 {
                     try
                     {
                         var result = await ApplyValidationRuleAsync(data, rule, strict, cancellationToken);
                         validationResults.Add(result);
                     }
                     catch (Exception ex)
                     {
                         errors.Add($"Error applying rule {rule.GetValueOrDefault("name")}: {ex.Message}");
                     }
                 }
             }

             // 5. 丰富的结果返回
             return ToolResult.Success(new
             {
                 isValid = !validationResults.Any(r => !r.IsValid),
                 validationResults,
                 errors,
                 summary = new
                 {
                     totalRules = rules.Count,
                     passedRules = validationResults.Count(r => r.IsValid),
                     failedRules = validationResults.Count(r => !r.IsValid),
                     hasErrors = errors.Any()
                 }
             });
         }
         catch (Exception ex)
         {
             // 6. 详细的错误信息
             return ToolResult.Failure($"Data validation failed: {ex.Message}", new List<ValidationError>
             {
                 new ValidationError("execution", ex.Message)
             });
         }
     }

     private async Task<ValidationResult> ApplyValidationRuleAsync(object data, Dictionary<string, object> rule, bool strict, CancellationToken cancellationToken)
     {
         var ruleName = rule.GetValueOrDefault("name")?.ToString() ?? "unnamed";
         var ruleType = rule.GetValueOrDefault("type")?.ToString();

         return ruleType?.ToLower() switch
         {
             "required" => ValidateRequired(data, rule),
             "range" => ValidateRange(data, rule),
             "pattern" => ValidatePattern(data, rule),
             "custom" => await ValidateCustomAsync(data, rule, cancellationToken),
             _ => new ValidationResult { IsValid = !strict, RuleName = ruleName, Message = $"Unknown rule type: {ruleType}" }
         };
     }
 }
 ```

 ### 2. 工具测试模板

 ```csharp
 [TestClass]
 public class DataValidatorToolTests
 {
     private DataValidatorTool _tool;
     private Mock<ILogger<DataValidatorTool>> _loggerMock;

     [TestInitialize]
     public void Setup()
     {
         _loggerMock = new Mock<ILogger<DataValidatorTool>>();
         _tool = new DataValidatorTool(_loggerMock.Object);
     }

     [TestMethod]
     public async Task ExecuteAsync_ValidInput_ReturnsSuccess()
     {
         // Arrange
         var parameters = new Dictionary<string, object>
         {
             ["data"] = new { name = "test", value = 42 },
             ["rules"] = new List<object>
             {
                 new Dictionary<string, object>
                 {
                     ["name"] = "required_name",
                     ["type"] = "required",
                     ["field"] = "name"
                 }
             }
         };

         // Act
         var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

         // Assert
         Assert.IsTrue(result.Success);
         Assert.IsNotNull(result.Data);
     }

     [TestMethod]
     public async Task ExecuteAsync_MissingInput_ReturnsFailure()
     {
         // Arrange
         var parameters = new Dictionary<string, object>
         {
             ["rules"] = new List<object>
             {
                 new Dictionary<string, object>
                 {
                     ["name"] = "required_name",
                     ["type"] = "required",
                     ["field"] = "name"
                 }
             }
         };

         // Act
         var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

         // Assert
         Assert.IsFalse(result.Success);
         Assert.IsTrue(result.ErrorMessage.Contains("required"));
     }

     [TestMethod]
     public async Task ValidateParametersAsync_ValidParameters_ReturnsValid()
     {
         // Arrange
         var parameters = new Dictionary<string, object>
         {
             ["data"] = new { name = "test", value = 42 },
             ["rules"] = new List<object>
             {
                 new Dictionary<string, object>
                 {
                     ["name"] = "required_name",
                     ["type"] = "required",
                     ["field"] = "name"
                 }
             }
         };

         // Act
         var result = await _tool.ValidateParametersAsync(parameters);

         // Assert
         Assert.IsTrue(result.IsValid);
         Assert.AreEqual(0, result.Errors.Count);
     }
 }
 ```

 ## 📚 总结

 工具系统是Aevatar Agent Framework的重要组成部分，提供了：

 1. **安全的工具执行环境**：多层验证、权限控制、审计日志
 2. **灵活的工具管理机制**：动态注册、自动发现、生命周期管理
 3. **高性能的执行引擎**：批处理、并行执行、容错机制
 4. **完善的监控体系**：指标收集、性能追踪、异常处理
 5. **丰富的扩展能力**：自定义工具、插件系统、配置管理

 通过遵循这些设计原则和最佳实践，可以构建出既安全又高效的工具系统，为AI代理提供强大的外部能力支持。

 ---

 *本文档详细描述了工具系统的设计，包括核心接口、安全机制、执行引擎和最佳实践，为开发和扩展工具功能提供全面指导。*
