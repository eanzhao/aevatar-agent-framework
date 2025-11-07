# Aevatar Agent Framework - 最佳实践指南

## 🎯 概述

本文档提供Aevatar Agent Framework的**最佳实践指南**，涵盖**开发、部署、运维**等各个阶段的最佳实践，帮助开发者构建**高质量、可维护、高性能**的代理应用。

## 📋 开发最佳实践

### 1. 代理设计最佳实践

#### 1.1 代理命名规范

```csharp
// ✅ 好的命名
public class CustomerServiceAgent : AIGAgentBase<CustomerServiceState>
public class OrderProcessingAgent : GAgentBase<OrderState>
public class DataAnalysisAgent : AIGAgentBase<AnalysisState>

// ❌ 不好的命名
public class Agent1 : GAgentBase<object>  // 无意义名称
public class MyAgent : GAgentBase<State>  // 过于通用
public class CSAgent : AIGAgentBase<State>  // 缩写不清晰
```

#### 1.2 状态设计原则

```csharp
// ✅ 好的状态设计
public class CustomerServiceState
{
    public string CustomerId { get; set; }
    public List<SupportTicket> ActiveTickets { get; set; } = new();
    public ConversationHistory Conversation { get; set; } = new();
    public CustomerPreferences Preferences { get; set; } = new();

    // 使用可空类型表示可选字段
    public DateTime? LastInteractionTime { get; set; }
    public Priority? CurrentPriority { get; set; }
}

// ❌ 不好的状态设计
public class BadState
{
    // 避免使用过于通用的名称
    public object Data { get; set; }  // 类型不清晰

    // 避免深层嵌套
    public Dictionary<string, Dictionary<string, List<object>>> ComplexStructure { get; set; }

    // 避免存储大量数据
    public List<byte[]> LargeFiles { get; set; }  // 应该存储引用
}
```

#### 1.3 事件设计最佳实践

```csharp
// ✅ 好的事件设计
public class CustomerTicketCreatedEvent : IEvent
{
    public string CustomerId { get; init; }
    public string TicketId { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public TicketPriority Priority { get; init; }
    public string Category { get; init; }
    public string Description { get; init; }
}

// 使用描述性的事件名称
public class OrderPaymentProcessedEvent : IEvent
public class UserAuthenticationFailedEvent : IEvent
public class SystemHealthCheckCompletedEvent : IEvent

// ❌ 不好的事件设计
public class Event1 : IEvent  // 无意义名称
public class DataUpdatedEvent : IEvent  // 过于通用
public class DoSomethingEvent : IEvent  // 命令式而非描述性
```

#### 1.4 事件处理器实现

```csharp
public class CustomerServiceAgent : AIGAgentBase<CustomerServiceState>
{
    // ✅ 好的事件处理器实现
    [EventHandler]
    private async Task HandleTicketCreatedAsync(CustomerTicketCreatedEvent @event)
    {
        // 1. 验证事件数据
        if (string.IsNullOrEmpty(@event.CustomerId))
        {
            Logger.LogWarning("Received ticket created event with empty customer ID");
            return;
        }

        // 2. 更新状态
        State.ActiveTickets.Add(new SupportTicket
        {
            Id = @event.TicketId,
            CustomerId = @event.CustomerId,
            Priority = @event.Priority,
            Category = @event.Category,
            CreatedAt = @event.CreatedAt
        });

        // 3. 执行业务逻辑
        var response = await ProcessNewTicketAsync(@event);

        // 4. 发布后续事件
        if (response.RequiresImmediateAttention)
        {
            await PublishAsync(new HighPriorityTicketReceivedEvent(@event.TicketId));
        }

        // 5. 记录日志
        Logger.LogInformation("Processed ticket creation for customer {@CustomerId}, ticket {@TicketId}",
            @event.CustomerId, @event.TicketId);
    }

    // ❌ 不好的事件处理器实现
    [EventHandler]
    private async Task BadHandlerAsync(object @event)
    {
        // 1. 过于通用的参数类型
        // 2. 没有验证
        // 3. 没有错误处理
        // 4. 没有日志记录
        // 5. 业务逻辑过于复杂
    }
}
```

### 2. AI代理最佳实践

#### 2.1 提示工程

```csharp
public class CustomerServiceAIAgent : AIGAgentBase<CustomerServiceState>
{
    protected override async Task EnrichContextAsync(AIContext context)
    {
        // ✅ 好的上下文构建

        // 1. 提供清晰的系统提示
        context.SystemPrompt = @"
        You are a helpful customer service assistant for {CompanyName}.
        You have access to the customer's information and support history.
        Always be polite, professional, and solution-oriented.
        If you cannot help with something, clearly explain why and offer alternatives.
        ";

        // 2. 添加上下文信息
        context.Metadata["customer_id"] = State.CustomerId;
        context.Metadata["ticket_count"] = State.ActiveTickets.Count;
        context.Metadata["company_name"] = "Aevatar Inc.";

        // 3. 包含相关历史
        var recentTickets = State.ActiveTickets.TakeLast(5).ToList();
        context.Metadata["recent_tickets"] = recentTickets;

        // 4. 设置明确的约束
        context.Metadata["response_constraints"] = new
        {
            max_length = 500,
            tone = "professional",
            language = "English",
            avoid = new[] { "technical_jargon", "negative_language" }
        };

        await base.EnrichContextAsync(context);
    }

    // ❌ 不好的上下文构建
    protected override Task BadContextEnrichmentAsync(AIContext context)
    {
        // 1. 过于复杂的提示
        context.SystemPrompt = @"
        You are an AI assistant that should help customers but also be aware of
        company policies and sometimes you need to escalate and sometimes solve
        directly and remember to always be nice but firm when needed and...
        "; // 过于冗长且不清晰

        // 2. 包含不必要的信息
        context.Metadata["irrelevant_data"] = GetUnnecessaryData();

        // 3. 没有明确的约束
        return Task.CompletedTask;
    }
}
```

#### 2.2 工具使用

```csharp
public class DataAnalysisAIAgent : AIGAgentBase<AnalysisState>
{
    protected override void ConfigureTools(IToolManager toolManager)
    {
        // ✅ 好的工具配置

        // 1. 注册核心工具
        toolManager.RegisterTool(new DataQueryTool());
        toolManager.RegisterTool(new StatisticalAnalysisTool());
        toolManager.RegisterTool(new VisualizationTool());

        // 2. 设置工具权限
        var sensitiveDataTool = new SensitiveDataAccessTool();
        sensitiveDataTool.RequiresConfirmation = true;
        sensitiveDataTool.RequiredRoles = new[] { "DataAnalyst", "Manager" };
        toolManager.RegisterTool(sensitiveDataTool);

        // 3. 配置工具参数验证
        var exportTool = new DataExportTool();
        exportTool.SetParameterValidation("format", value =>
            new[] { "csv", "json", "xml" }.Contains(value?.ToString()?.ToLower()));
        exportTool.SetParameterValidation("max_rows", value =>
            int.TryParse(value?.ToString(), out var rows) && rows > 0 && rows <= 1000000);
        toolManager.RegisterTool(exportTool);
    }

    protected override async Task<List<ToolCall>> FilterToolCallsAsync(
        List<ToolCall> toolCalls, AIContext context)
    {
        // ✅ 工具调用过滤
        var filteredCalls = new List<ToolCall>();

        foreach (var toolCall in toolCalls)
        {
            // 1. 验证工具调用的合理性
            if (!IsToolCallReasonable(toolCall, context))
            {
                Logger.LogWarning("Filtered out unreasonable tool call: {ToolName}", toolCall.Name);
                continue;
            }

            // 2. 检查权限
            if (!await HasToolPermissionAsync(toolCall, context))
            {
                Logger.LogWarning("Filtered out unauthorized tool call: {ToolName}", toolCall.Name);
                continue;
            }

            // 3. 检查速率限制
            if (!await CheckRateLimitAsync(toolCall))
            {
                Logger.LogWarning("Filtered out rate-limited tool call: {ToolName}", toolCall.Name);
                continue;
            }

            filteredCalls.Add(toolCall);
        }

        return filteredCalls;
    }
}
```

#### 2.3 内存管理

```csharp
public class ConversationAIAgent : AIGAgentBase<ConversationState>
{
    protected override async Task UpdateMemoryAsync(AIRequest request, ProcessingResult result)
    {
        // ✅ 好的内存管理

        // 1. 存储关键对话信息
        var keyPoints = ExtractKeyPoints(result.Response);
        if (keyPoints.Any())
        {
            await Memory.AddToWorkingMemoryAsync(Id, keyPoints.Select(kp => new MemoryItem
            {
                Id = Guid.NewGuid().ToString(),
                Type = "conversation_key_point",
                Content = kp.Content,
                Metadata = new Dictionary<string, object>
                {
                    ["confidence"] = kp.Confidence,
                    ["timestamp"] = DateTime.UtcNow,
                    ["conversation_id"] = request.ConversationId
                }
            }).ToList());
        }

        // 2. 管理对话历史长度
        var maxHistoryLength = 50;
        var conversationHistory = await Memory.GetConversationHistoryAsync(Id, maxHistoryLength);
        if (conversationHistory.Count >= maxHistoryLength)
        {
            // 归档旧的对话
            var oldConversations = conversationHistory.Take(conversationHistory.Count - maxHistoryLength + 10).ToList();
            await ArchiveConversationsAsync(oldConversations);
        }

        // 3. 提取实体并存储到长期记忆
        var entities = ExtractEntities(result.Response);
        foreach (var entity in entities)
        {
            await Memory.StoreInLongTermMemoryAsync(Id, new MemoryItem
            {
                Id = Guid.NewGuid().ToString(),
                Type = "entity",
                Content = entity.Name,
                Metadata = new Dictionary<string, object>
                {
                    ["entity_type"] = entity.Type,
                    ["confidence"] = entity.Confidence,
                    ["source"] = "conversation"
                }
            });
        }

        await base.UpdateMemoryAsync(request, result);
    }

    private List<KeyPoint> ExtractKeyPoints(string response)
    {
        // 实现关键点提取逻辑
        // 使用NLP技术或简单的关键词匹配
        var keyPoints = new List<KeyPoint>();

        // 示例：提取重要声明
        var importantPatterns = new[]
        {
            @"i need\s+(.+)",
            @"i want\s+(.+)",
            @"please\s+(.+)",
            @"help me\s+(.+)"
        };

        foreach (var pattern in importantPatterns)
        {
            var match = Regex.Match(response, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                keyPoints.Add(new KeyPoint
                {
                    Content = match.Groups[1].Value.Trim(),
                    Confidence = 0.8,
                    Type = "user_intent"
                });
            }
        }

        return keyPoints;
    }
}
```

### 3. 工具开发最佳实践

#### 3.1 工具设计原则

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

#### 3.2 工具安全实践

```csharp
[Tool("DatabaseQuery", "Executes database queries", RequiresAuthentication = true)]
public class DatabaseQueryTool : AevatarToolBase
{
    public DatabaseQueryTool(ILogger<DatabaseQueryTool> logger) : base(logger)
    {
        // 安全设置
        _definition.RequiresAuthentication = true;
        _definition.RequiredRoles = new[] { "DatabaseAdmin", "Developer" };
        _definition.RequiredPermissions = new[] { "database.read", "database.write" };
        _definition.RequiresConfirmation = true;
        _definition.Timeout = TimeSpan.FromMinutes(2);
        _definition.MaxRetryCount = 1;
    }

    protected override async Task<bool> CheckPermissionAsync(IPrincipal principal, string permission, Dictionary<string, object> parameters)
    {
        // ✅ 安全的权限检查

        // 1. 检查写操作权限
        if (permission == "database.write")
        {
            var query = parameters.GetValueOrDefault("query")?.ToString() ?? "";

            // 检查是否为写操作
            if (IsWriteQuery(query))
            {
                // 需要额外的写权限
                if (!principal.IsInRole("DatabaseAdmin"))
                {
                    Logger.LogWarning("User {User} attempted write operation without DatabaseAdmin role", principal.Identity.Name);
                    return false;
                }

                // 检查危险操作
                if (IsDangerousQuery(query))
                {
                    Logger.LogWarning("User {User} attempted dangerous query: {Query}", principal.Identity.Name, SanitizeQuery(query));
                    return false;
                }
            }
        }

        // 2. 检查数据库访问权限
        var database = parameters.GetValueOrDefault("database")?.ToString();
        if (!string.IsNullOrEmpty(database) && !HasDatabaseAccess(principal, database))
        {
            Logger.LogWarning("User {User} attempted to access unauthorized database: {Database}",
                principal.Identity.Name, database);
            return false;
        }

        return await base.CheckPermissionAsync(principal, permission, parameters);
    }

    private bool IsWriteQuery(string query)
    {
        var writeKeywords = new[] { "INSERT", "UPDATE", "DELETE", "DROP", "CREATE", "ALTER" };
        var upperQuery = query.ToUpperInvariant();
        return writeKeywords.Any(keyword => upperQuery.Contains(keyword));
    }

    private bool IsDangerousQuery(string query)
    {
        var dangerousPatterns = new[]
        {
            @"DROP\s+DATABASE",
            @"DROP\s+TABLE",
            @"TRUNCATE\s+TABLE",
            @"DELETE\s+FROM.*WHERE.*1\s*=\s*1",
            @"UPDATE.*SET.*=.*WHERE.*1\s*=\s*1"
        };

        return dangerousPatterns.Any(pattern => Regex.IsMatch(query, pattern, RegexOptions.IgnoreCase));
    }

    private string SanitizeQuery(string query)
    {
        // 移除敏感信息
        var sanitized = Regex.Replace(query, @"'(.*?)'", "'***'");
        sanitized = Regex.Replace(sanitized, @"\b\d+\b", "***");
        return sanitized;
    }
}
```

## 🚀 性能最佳实践

### 1. 事件处理性能优化

```csharp
public class OptimizedEventProcessor
{
    private readonly Channel<EventEnvelope> _eventChannel;
    private readonly SemaphoreSlim _processingSemaphore;
    private readonly int _maxConcurrency;

    public OptimizedEventProcessor(int maxConcurrency = 10)
    {
        _maxConcurrency = maxConcurrency;
        _processingSemaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);

        // 使用有界通道防止内存溢出
        _eventChannel = Channel.CreateBounded<EventEnvelope>(1000);
    }

    public async Task ProcessEventsAsync(CancellationToken cancellationToken = default)
    {
        var consumerTasks = new Task[_maxConcurrency];

        // 启动多个消费者
        for (int i = 0; i < _maxConcurrency; i++)
        {
            consumerTasks[i] = ProcessEventsConsumerAsync(cancellationToken);
        }

        await Task.WhenAll(consumerTasks);
    }

    private async Task ProcessEventsConsumerAsync(CancellationToken cancellationToken)
    {
        await foreach (var envelope in _eventChannel.Reader.ReadAllAsync(cancellationToken))
        {
            await _processingSemaphore.WaitAsync(cancellationToken);

            try
            {
                // 使用TryExecuteAsync避免异常传播
                var result = await TryExecuteAsync(() => ProcessEventAsync(envelope, cancellationToken));

                if (!result.Success)
                {
                    // 处理失败的事件
                    await HandleProcessingFailureAsync(envelope, result.Exception);
                }
            }
            finally
            {
                _processingSemaphore.Release();
            }
        }
    }

    private async Task<ExecutionResult> ProcessEventAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        // ✅ 性能优化的事件处理

        // 1. 使用对象池减少GC压力
        var context = ObjectPool<EventContext>.Shared.Get();
        try
        {
            context.Initialize(envelope);

            // 2. 并行处理多个处理器
            var handlers = GetEventHandlers(envelope.EventType);
            var handlerTasks = handlers.Select(handler =>
                ExecuteHandlerAsync(handler, context, cancellationToken));

            var results = await Task.WhenAll(handlerTasks);

            // 3. 批量发布结果事件
            var resultEvents = results.Where(r => r.HasResultEvent).Select(r => r.ResultEvent).ToList();
            if (resultEvents.Any())
            {
                await PublishBatchAsync(resultEvents);
            }

            return ExecutionResult.Success();
        }
        finally
        {
            ObjectPool<EventContext>.Shared.Return(context);
        }
    }

    private async Task<HandlerResult> ExecuteHandlerAsync(IEventHandler handler, EventContext context, CancellationToken cancellationToken)
    {
        // 使用Activity进行性能监控
        using var activity = ActivitySource.StartActivity($"Handle {context.Envelope.EventType}");

        try
        {
            // 检查缓存避免重复处理
            if (await IsDuplicateAsync(context.Envelope.Id))
            {
                return HandlerResult.Skipped("Duplicate event");
            }

            // 执行处理器
            await handler.HandleAsync(context.Envelope, cancellationToken);

            // 记录处理成功
            await RecordProcessedAsync(context.Envelope.Id);

            return HandlerResult.Success();
        }
        catch (Exception ex)
        {
            // 记录异常但不传播，保持处理流程
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return HandlerResult.Failed(ex);
        }
    }
}
```

### 2. 内存使用优化

```csharp
public class MemoryOptimizedAgent : AIGAgentBase<OptimizedState>
{
    private readonly IMemoryCache _cache;
    private readonly ObjectPool<StringBuilder> _stringBuilderPool;
    private readonly ArrayPool<byte> _arrayPool;

    public MemoryOptimizedAgent(IMemoryCache cache)
    {
        _cache = cache;
        _stringBuilderPool = new DefaultObjectPoolProvider().Create(new StringBuilderPooledObjectPolicy());
        _arrayPool = ArrayPool<byte>.Shared;
    }

    protected override async Task ProcessLargeDataAsync(byte[] data)
    {
        // ✅ 内存优化的数据处理

        // 1. 使用ArrayPool避免大数组分配
        var buffer = _arrayPool.Rent(data.Length);
        try
        {
            Array.Copy(data, buffer, data.Length);

            // 2. 流式处理而非一次性加载
            await ProcessDataInChunksAsync(buffer, data.Length);
        }
        finally
        {
            _arrayPool.Return(buffer, clearArray: true);
        }
    }

    private async Task ProcessDataInChunksAsync(byte[] buffer, int length)
    {
        const int chunkSize = 4096; // 4KB chunks
        var chunks = (length + chunkSize - 1) / chunkSize;

        for (int i = 0; i < chunks; i++)
        {
            var offset = i * chunkSize;
            var remaining = Math.Min(chunkSize, length - offset);

            // 处理数据块
            await ProcessChunkAsync(buffer, offset, remaining);

            // 定期让出控制权，避免阻塞
            if (i % 10 == 0)
            {
                await Task.Yield();
            }
        }
    }

    protected override async Task<string> BuildLargeResponseAsync()
    {
        // 3. 使用StringBuilder池
        var sb = _stringBuilderPool.Get();
        try
        {
            // 构建响应
            foreach (var item in State.Items)
            {
                sb.AppendLine($"Item: {item.Name}, Value: {item.Value}");

                // 定期刷新避免内存累积
                if (sb.Length > 8192)
                {
                    await FlushStringBuilderAsync(sb);
                }
            }

            return sb.ToString();
        }
        finally
        {
            _stringBuilderPool.Return(sb);
        }
    }

    protected override async Task<List<ProcessedData>> ProcessBatchAsync(List<RawData> batch)
    {
        // 4. 使用异步流避免大量数据累积
        var results = new List<ProcessedData>();

        await foreach (var processedItem in ProcessBatchStreamAsync(batch))
        {
            results.Add(processedItem);

            // 限制结果集大小
            if (results.Count >= 1000)
            {
                await PublishPartialResultsAsync(results);
                results.Clear();
            }
        }

        return results;
    }

    private async IAsyncEnumerable<ProcessedData> ProcessBatchStreamAsync(List<RawData> batch)
    {
        foreach (var item in batch)
        {
            // 异步处理每个项目
            var processed = await ProcessItemAsync(item);
            yield return processed;

            // 定期让出控制权
            if (batch.IndexOf(item) % 100 == 0)
            {
                await Task.Yield();
            }
        }
    }
}
```

### 3. 数据库访问优化

```csharp
public class DatabaseOptimizedAgent : AIGAgentBase<DatabaseState>
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly IMemoryCache _cache;

    public DatabaseOptimizedAgent(IDbContextFactory<ApplicationDbContext> contextFactory, IMemoryCache cache)
    {
        _contextFactory = contextFactory;
        _cache = cache;
    }

    [EventHandler]
    private async Task HandleDataQueryAsync(DataQueryEvent @event)
    {
        // ✅ 数据库访问优化

        // 1. 使用缓存避免重复查询
        var cacheKey = $"query_{@event.QueryId}_{@event.Parameters.GetHashCode()}";
        if (_cache.TryGetValue(cacheKey, out var cachedResult))
        {
            await PublishAsync(new QueryResultEvent(@event.QueryId, cachedResult));
            return;
        }

        // 2. 使用异步数据库上下文
        await using var dbContext = await _contextFactory.CreateDbContextAsync();

        // 3. 使用编译查询提高性能
        var result = await _compiledQueries.GetOrAdd(@event.QueryType, type => CompileQuery(type))
            .Invoke(dbContext, @event.Parameters);

        // 4. 只选择需要的字段
        var projectedResult = result.Select(r => new
        {
            r.Id,
            r.Name,
            r.Status,
            // 避免选择大字段
            // r.LargeDataField
        }).ToList();

        // 5. 缓存结果
        _cache.Set(cacheKey, projectedResult, TimeSpan.FromMinutes(5));

        await PublishAsync(new QueryResultEvent(@event.QueryId, projectedResult));
    }

    // 编译查询缓存
    private static readonly ConcurrentDictionary<string, Func<ApplicationDbContext, Dictionary<string, object>, Task<List<Result>>>> _compiledQueries = new();

    private Func<ApplicationDbContext, Dictionary<string, object>, Task<List<Result>>> CompileQuery(string queryType)
    {
        return EF.CompileAsyncQuery((ApplicationDbContext context, Dictionary<string, object> parameters) =>
        {
            return context.Results
                .Where(r => r.Type == queryType)
                .Where(r => r.CreatedAt >= (DateTime)parameters["fromDate"])
                .Where(r => r.Status == (string)parameters["status"])
                .OrderByDescending(r => r.CreatedAt)
                .Take((int)parameters["limit"])
                .ToList();
        });
    }

    protected override async Task BulkInsertAsync(List<DataItem> items)
    {
        // 6. 批量插入优化
        const int batchSize = 1000;

        await using var dbContext = await _contextFactory.CreateDbContextAsync();

        for (int i = 0; i < items.Count; i += batchSize)
        {
            var batch = items.Skip(i).Take(batchSize).ToList();

            // 使用批量插入
            await dbContext.BulkInsertAsync(batch, cancellationToken: default);

            // 定期保存和清理更改跟踪
            if (i % 5000 == 0)
            {
                await dbContext.SaveChangesAsync();
                dbContext.ChangeTracker.Clear();
            }
        }

        await dbContext.SaveChangesAsync();
    }
}
```

## 🔐 安全最佳实践

### 1. 输入验证与清理

```csharp
public class SecureAgent : AIGAgentBase<SecureState>
{
    private readonly IInputValidator _inputValidator;
    private readonly ISanitizer _sanitizer;

    protected override async Task HandleEventAsync(IEvent @event)
    {
        // ✅ 安全的输入处理

        // 1. 事件数据验证
        var validationResult = await _inputValidator.ValidateAsync(@event);
        if (!validationResult.IsValid)
        {
            Logger.LogWarning("Invalid event received: {Errors}", validationResult.Errors);
            await PublishAsync(new InvalidEventReceivedEvent(@event.EventType, validationResult.Errors));
            return;
        }

        // 2. 清理敏感数据
        var sanitizedEvent = await SanitizeEventAsync(@event);

        // 3. 类型安全的处理
        switch (sanitizedEvent)
        {
            case UserInputEvent inputEvent:
                await HandleUserInputAsync(inputEvent);
                break;

            case SystemCommandEvent commandEvent:
                await HandleSystemCommandAsync(commandEvent);
                break;

            default:
                await base.HandleEventAsync(sanitizedEvent);
                break;
        }
    }

    private async Task HandleUserInputAsync(UserInputEvent @event)
    {
        // 4. 用户输入清理
        var sanitizedInput = _sanitizer.Sanitize(@event.Input);

        // 5. 防止注入攻击
        if (ContainsPotentialInjection(sanitizedInput))
        {
            Logger.LogWarning("Potential injection attempt detected: {Input}", sanitizedInput);
            await PublishAsync(new SecurityAlertEvent("injection_attempt", sanitizedInput));
            return;
        }

        // 6. 长度限制
        if (sanitizedInput.Length > 10000)
        {
            Logger.LogWarning("Input too long: {Length}", sanitizedInput.Length);
            sanitizedInput = sanitizedInput.Substring(0, 10000);
        }

        await ProcessInputAsync(sanitizedInput);
    }

    private bool ContainsPotentialInjection(string input)
    {
        var dangerousPatterns = new[]
        {
            @"\u003cscript\u003e", @"\u003c\/script\u003e",
            @"javascript:", @"vbscript:",
            @"onload=", @"onerror=", @"onclick=",
            @"\\x", @"\\u", // 编码注入
            @"union\s+select", @"drop\s+table", // SQL注入
            @"exec\s*\(", @"xp_", // 命令注入
        };

        return dangerousPatterns.Any(pattern =>
            Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase));
    }
}
```

### 2. 认证与授权

```csharp
[Tool("SensitiveDataAccess", "Accesses sensitive system data", RequiresAuthentication = true)]
public class SensitiveDataAccessTool : AevatarToolBase
{
    private readonly IAuthorizationService _authorizationService;
    private readonly IAuditLogger _auditLogger;

    public SensitiveDataAccessTool(
        IAuthorizationService authorizationService,
        IAuditLogger auditLogger,
        ILogger<SensitiveDataAccessTool> logger) : base(logger)
    {
        _authorizationService = authorizationService;
        _auditLogger = auditLogger;

        // 安全要求
        _definition.RequiresAuthentication = true;
        _definition.RequiredRoles = new[] { "SystemAdmin", "SecurityOfficer" };
        _definition.RequiredPermissions = new[] { "sensitive.data.read", "audit.access" };
        _definition.RequiresConfirmation = true;
    }

    protected override async Task<bool> HasPermissionAsync(IPrincipal principal, Dictionary<string, object> parameters)
    {
        // ✅ 严格的权限检查

        // 1. 验证用户身份
        if (!principal.Identity.IsAuthenticated)
        {
            Logger.LogWarning("Unauthenticated access attempt to sensitive data tool");
            return false;
        }

        var userId = principal.Identity.Name;

        // 2. 检查角色权限
        var hasRequiredRole = _definition.RequiredRoles.Any(role => principal.IsInRole(role));
        if (!hasRequiredRole)
        {
            Logger.LogWarning("User {UserId} lacks required role for sensitive data access", userId);
            await _auditLogger.LogSecurityEventAsync("unauthorized_role_access", userId, new { requiredRoles = _definition.RequiredRoles });
            return false;
        }

        // 3. 检查细粒度权限
        foreach (var permission in _definition.RequiredPermissions)
        {
            var hasPermission = await _authorizationService.CheckPermissionAsync(userId, permission);
            if (!hasPermission)
            {
                Logger.LogWarning("User {UserId} lacks required permission: {Permission}", userId, permission);
                await _auditLogger.LogSecurityEventAsync("unauthorized_permission_access", userId, new { requiredPermission = permission });
                return false;
            }
        }

        // 4. 检查数据特定权限
        var dataType = parameters.GetValueOrDefault("dataType")?.ToString();
        if (!string.IsNullOrEmpty(dataType))
        {
            var hasDataAccess = await _authorizationService.CheckDataAccessAsync(userId, dataType);
            if (!hasDataAccess)
            {
                Logger.LogWarning("User {UserId} lacks access to data type: {DataType}", userId, dataType);
                await _auditLogger.LogSecurityEventAsync("unauthorized_data_access", userId, new { dataType });
                return false;
            }
        }

        // 5. 检查时间限制
        var accessTimeRestriction = await _authorizationService.GetAccessTimeRestrictionAsync(userId);
        if (accessTimeRestriction != null)
        {
            var currentTime = DateTime.UtcNow.TimeOfDay;
            if (currentTime < accessTimeRestriction.StartTime || currentTime > accessTimeRestriction.EndTime)
            {
                Logger.LogWarning("User {UserId} attempted access outside allowed hours", userId);
                await _auditLogger.LogSecurityEventAsync("access_outside_allowed_hours", userId, new { currentTime });
                return false;
            }
        }

        // 记录成功的权限检查
        await _auditLogger.LogSecurityEventAsync("permission_check_passed", userId, new
        {
            tool = _definition.Name,
            permissions = _definition.RequiredPermissions,
            dataType
        });

        return true;
    }

    public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
    {
        var userId = Thread.CurrentPrincipal?.Identity?.Name ?? "unknown";
        var dataType = parameters.GetValueOrDefault("dataType")?.ToString();
        var query = parameters.GetValueOrDefault("query")?.ToString();

        try
        {
            // 记录工具执行
            await _auditLogger.LogToolExecutionAsync(userId, _definition.Name, parameters);

            // 执行工具逻辑
            var result = await ExecuteDataAccessAsync(dataType, query, cancellationToken);

            // 记录成功
            await _auditLogger.LogSecurityEventAsync("sensitive_data_access_success", userId, new
            {
                dataType,
                resultCount = result?.Data ?? 0
            });

            return result;
        }
        catch (Exception ex)
        {
            // 记录失败
            await _auditLogger.LogSecurityEventAsync("sensitive_data_access_failed", userId, new
            {
                dataType,
                error = ex.Message
            });

            throw;
        }
    }
}
```

### 3. 数据保护

```csharp
public class DataProtectionService
{
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IKeyManager _keyManager;

    public DataProtectionService(IDataProtectionProvider dataProtectionProvider, IKeyManager keyManager)
    {
        _dataProtectionProvider = dataProtectionProvider;
        _keyManager = keyManager;
    }

    public string ProtectSensitiveData(string data, string purpose)
    {
        // ✅ 数据保护最佳实践

        // 1. 使用强加密
        var protector = _dataProtectionProvider.CreateProtector(purpose);

        // 2. 添加时间戳防止重放
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var dataWithTimestamp = $"{timestamp}:{data}";

        // 3. 加密数据
        var encrypted = protector.Protect(dataWithTimestamp);

        // 4. 添加完整性检查
        var hash = ComputeHash(dataWithTimestamp);
        var protectedData = $"{encrypted}:{hash}";

        return protectedData;
    }

    public string UnprotectSensitiveData(string protectedData, string purpose)
    {
        try
        {
            // 1. 解析保护的数据
            var parts = protectedData.Split(':');
            if (parts.Length != 2)
            {
                throw new SecurityException("Invalid protected data format");
            }

            var encrypted = parts[0];
            var expectedHash = parts[1];

            // 2. 解密数据
            var protector = _dataProtectionProvider.CreateProtector(purpose);
            var decrypted = protector.Unprotect(encrypted);

            // 3. 验证完整性
            var actualHash = ComputeHash(decrypted);
            if (actualHash != expectedHash)
            {
                throw new SecurityException("Data integrity check failed");
            }

            // 4. 验证时间戳
            var timestampParts = decrypted.Split(':');
            if (timestampParts.Length >= 2 && long.TryParse(timestampParts[0], out var timestamp))
            {
                var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var timeDifference = Math.Abs(currentTime - timestamp);

                // 如果数据太旧，可能是重放攻击
                if (timeDifference > 3600) // 1小时
                {
                    throw new SecurityException("Data timestamp is too old");
                }

                // 返回原始数据（移除时间戳）
                return string.Join(":", timestampParts.Skip(1));
            }

            return decrypted;
        }
        catch (Exception ex)
        {
            throw new SecurityException($"Failed to unprotect data: {ex.Message}", ex);
        }
    }

    public async Task<SecureDataContainer> SecureDataAsync(object data, string purpose, TimeSpan? expiration = null)
    {
        // 5. 安全的数据容器
        var serializedData = JsonSerializer.Serialize(data);
        var protectedData = ProtectSensitiveData(serializedData, purpose);

        var container = new SecureDataContainer
        {
            Id = Guid.NewGuid().ToString(),
            ProtectedData = protectedData,
            Purpose = purpose,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiration.HasValue ? DateTime.UtcNow.Add(expiration.Value) : null,
            EncryptionKeyId = await _keyManager.GetCurrentKeyIdAsync()
        };

        return container;
    }

    private string ComputeHash(string data)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(hashBytes);
    }
}
```

## 📊 监控与可观测性最佳实践

### 1. 结构化日志

```csharp
public class ObservableAgent : AIGAgentBase<ObservableState>
{
    private static readonly ActivitySource ActivitySource = new("Aevatar.Agent.Processing");

    protected override async Task HandleEventAsync(IEvent @event)
    {
        // ✅ 可观测的事件处理

        using var activity = ActivitySource.StartActivity($"Process {@event.GetType().Name}");

        // 1. 添加活动标签
        activity?.SetTag("event.type", @event.GetType().Name);
        activity?.SetTag("event.id", @event.EventType);
        activity?.SetTag("agent.id", Id);
        activity?.SetTag("agent.type", GetType().Name);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 2. 使用日志范围
            using (Logger.BeginScope(new Dictionary<string, object>
            {
                ["EventId"] = @event.EventType,
                ["EventType"] = @event.GetType().Name,
                ["AgentId"] = Id,
                ["CorrelationId"] = Activity.Current?.TraceId.ToString()
            }))
            {
                Logger.LogInformation("Starting to process event {EventType}", @event.GetType().Name);

                // 处理事件
                await base.HandleEventAsync(@event);

                stopwatch.Stop();

                // 3. 记录处理成功
                Logger.LogInformation("Successfully processed event {EventType} in {Duration}ms",
                    @event.GetType().Name, stopwatch.ElapsedMilliseconds);

                // 4. 记录指标
                activity?.SetTag("processing.duration_ms", stopwatch.ElapsedMilliseconds);
                activity?.SetTag("processing.success", true);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // 5. 记录异常信息
            Logger.LogError(ex, "Failed to process event {EventType} after {Duration}ms",
                @event.GetType().Name, stopwatch.ElapsedMilliseconds);

            // 6. 设置活动状态
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("processing.duration_ms", stopwatch.ElapsedMilliseconds);
            activity?.SetTag("processing.success", false);
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.SetTag("error.message", ex.Message);

            throw;
        }
    }
}

// 自定义日志格式化器
public class AgentLogFormatter : ILogFormatter
{
    public string Format(LogEntry entry)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{entry.Level}] {entry.Message}");

        // 添加结构化数据
        if (entry.Properties?.Any() == true)
        {
            sb.AppendLine("Properties:");
            foreach (var prop in entry.Properties)
            {
                sb.AppendLine($"  {prop.Key}: {prop.Value}");
            }
        }

        // 添加异常信息
        if (entry.Exception != null)
        {
            sb.AppendLine($"Exception: {entry.Exception.GetType().Name}");
            sb.AppendLine($"Message: {entry.Exception.Message}");
            sb.AppendLine($"StackTrace: {entry.Exception.StackTrace}");
        }

        return sb.ToString();
    }
}
```

### 2. 指标收集

```csharp
public class MetricsCollector
{
    private readonly IMeterProvider _meterProvider;
    private readonly Meter _meter;
    private readonly Counter<long> _eventCounter;
    private readonly Histogram<double> _processingTimeHistogram;
    private readonly ObservableGauge<int> _activeAgentsGauge;

    public MetricsCollector(IMeterProvider meterProvider)
    {
        _meterProvider = meterProvider;
        _meter = _meterProvider.GetMeter("Aevatar.Agent.Metrics");

        // 创建指标
        _eventCounter = _meter.CreateCounter<long>(
            "aevatar.events.processed",
            description: "Total number of events processed");

        _processingTimeHistogram = _meter.CreateHistogram<double>(
            "aevatar.processing.duration",
            unit: "ms",
            description: "Event processing duration in milliseconds");

        _activeAgentsGauge = _meter.CreateObservableGauge<int>(
            "aevatar.agents.active",
            () => GetActiveAgentCount(),
            description: "Number of currently active agents");
    }

    public void RecordEventProcessed(string eventType, string agentType, long durationMs, bool success)
    {
        var tags = new TagList
        {
            { "event.type", eventType },
            { "agent.type", agentType },
            { "success", success }
        };

        _eventCounter.Add(1, tags);
        _processingTimeHistogram.Record(durationMs, tags);
    }

    public void RecordToolExecution(string toolName, string agentId, double durationMs, bool success, Exception exception = null)
    {
        var tags = new TagList
        {
            { "tool.name", toolName },
            { "agent.id", agentId },
            { "success", success }
        };

        if (exception != null)
        {
            tags.Add("error.type", exception.GetType().Name);
        }

        var toolCounter = _meter.CreateCounter<long>(
            "aevatar.tools.executions",
            description: "Tool execution count");

        var toolHistogram = _meter.CreateHistogram<double>(
            "aevatar.tools.duration",
            unit: "ms",
            description: "Tool execution duration");

        toolCounter.Add(1, tags);
        toolHistogram.Record(durationMs, tags);
    }

    public void RecordMemoryUsage(string agentId, long memoryBytes, string memoryType)
    {
        var memoryGauge = _meter.CreateObservableGauge<long>(
            "aevatar.memory.usage",
            () => new Measurement<long>(memoryBytes, new TagList { { "agent.id", agentId }, { "type", memoryType } }),
            unit: "By",
            description: "Memory usage by agent");
    }

    private int GetActiveAgentCount()
    {
        // 实现获取活跃代理数量的逻辑
        return AgentRegistry.GetActiveAgentCount();
    }
}
```

### 3. 分布式追踪

```csharp
public class DistributedTracingAgent : AIGAgentBase<TracedState>
{
    private static readonly ActivitySource ActivitySource = new("Aevatar.Agent.Distributed");

    protected override async Task ProcessAIAsync(AIRequest request)
    {
        // ✅ 分布式追踪

        using var activity = ActivitySource.StartActivity($"AI Process: {request.Intent}");

        // 1. 设置追踪上下文
        activity?.SetTag("ai.request.id", request.Id);
        activity?.SetTag("ai.request.intent", request.Intent);
        activity?.SetTag("ai.request.model", request.Model);
        activity?.SetTag("ai.request.max_tokens", request.MaxTokens);

        try
        {
            // 2. 创建链接的追踪活动
            using var contextActivity = ActivitySource.StartActivity("Build AI Context");
            var context = await BuildAIContextAsync(request);
            contextActivity?.SetTag("context.tools.count", context.AvailableTools?.Count ?? 0);
            contextActivity?.SetTag("context.memory.items", context.WorkingMemory?.Count ?? 0);

            // 3. 追踪LLM调用
            using var llmActivity = ActivitySource.StartActivity("LLM Generation");
            llmActivity?.SetTag("llm.provider", _llmProvider.GetType().Name);
            llmActivity?.SetTag("llm.model", request.Model);
            llmActivity?.SetTag("llm.temperature", request.Temperature);

            var stopwatch = Stopwatch.StartNew();
            var response = await _llmProvider.GenerateChatAsync(CreateChatRequest(context));
            stopwatch.Stop();

            llmActivity?.SetTag("llm.response_time_ms", stopwatch.ElapsedMilliseconds);
            llmActivity?.SetTag("llm.tokens.used", response.TokenUsage?.TotalTokens ?? 0);

            // 4. 追踪工具执行
            if (response.ToolCalls?.Any() == true)
            {
                using var toolsActivity = ActivitySource.StartActivity("Tool Execution");
                toolsActivity?.SetTag("tools.count", response.ToolCalls.Count);

                var toolResults = await ExecuteToolsAsync(response.ToolCalls);

                toolsActivity?.SetTag("tools.successful", toolResults.Count(r => r.Success));
                toolsActivity?.SetTag("tools.failed", toolResults.Count(r => !r.Success));
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return response;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.SetTag("error.message", ex.Message);
            activity?.SetTag("error.stacktrace", ex.StackTrace);

            throw;
        }
    }

    // 添加追踪信息到事件
    protected override async Task PublishAsync<TEvent>(TEvent @event, EventDirection direction = EventDirection.Bidirectional)
    {
        // 将追踪上下文添加到事件
        if (Activity.Current != null)
        {
            var envelope = new EventEnvelope
            {
                Event = @event,
                TraceContext = Activity.Current.Context,
                TraceId = Activity.Current.TraceId.ToString(),
                SpanId = Activity.Current.SpanId.ToString()
            };

            await base.PublishAsync(envelope, direction);
        }
        else
        {
            await base.PublishAsync(@event, direction);
        }
    }
}
```

## 🚀 部署最佳实践

### 1. 容器化部署

```dockerfile
# Dockerfile 最佳实践
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app

# 创建非root用户
RUN useradd -m -s /bin/bash aevatar

# 设置文件权限
RUN chown -R aevatar:aevatar /app
USER aevatar

# 健康检查
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 复制和还原包
COPY ["Aevatar.Agent.Service/Aevatar.Agent.Service.csproj", "Aevatar.Agent.Service/"]
RUN dotnet restore "Aevatar.Agent.Service/Aevatar.Agent.Service.csproj"

# 复制源代码
COPY . .
WORKDIR "/src/Aevatar.Agent.Service"
RUN dotnet build "Aevatar.Agent.Service.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Aevatar.Agent.Service.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# 设置环境变量
ENV DOTNET_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "Aevatar.Agent.Service.dll"]
```

### 2. Kubernetes部署配置

```yaml
# agent-deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: aevatar-agent
  labels:
    app: aevatar-agent
    version: v1.0.0
spec:
  replicas: 3
  strategy:
    type: RollingUpdate
    rollingUpdate:
      maxUnavailable: 1
      maxSurge: 1
  selector:
    matchLabels:
      app: aevatar-agent
  template:
    metadata:
      labels:
        app: aevatar-agent
        version: v1.0.0
      annotations:
        prometheus.io/scrape: "true"
        prometheus.io/port: "9090"
        prometheus.io/path: "/metrics"
    spec:
      securityContext:
        runAsNonRoot: true
        runAsUser: 1000
        fsGroup: 2000
      containers:
      - name: agent
        image: aevatar/agent:latest
        imagePullPolicy: Always
        ports:
        - containerPort: 8080
          name: http
        - containerPort: 9090
          name: metrics
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: "Production"
        - name: AGENT_RUNTIME_TYPE
          value: "Orleans"
        - name: ORLEANS_CLUSTER_ID
          valueFrom:
            secretKeyRef:
              name: agent-secrets
              key: cluster-id
        resources:
          requests:
            memory: "512Mi"
            cpu: "250m"
          limits:
            memory: "2Gi"
            cpu: "1000m"
        livenessProbe:
          httpGet:
            path: /health/live
            port: 8080
          initialDelaySeconds: 30
          periodSeconds: 10
          timeoutSeconds: 5
          failureThreshold: 3
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 8080
          initialDelaySeconds: 5
          periodSeconds: 5
          timeoutSeconds: 3
          failureThreshold: 3
        volumeMounts:
        - name: config-volume
          mountPath: /app/config
          readOnly: true
        - name: data-volume
          mountPath: /app/data
        securityContext:
          allowPrivilegeEscalation: false
          readOnlyRootFilesystem: true
          capabilities:
            drop:
            - ALL
      volumes:
      - name: config-volume
        configMap:
          name: agent-config
      - name: data-volume
        persistentVolumeClaim:
          claimName: agent-data-pvc
---
apiVersion: v1
kind: Service
metadata:
  name: aevatar-agent-service
  labels:
    app: aevatar-agent
spec:
  selector:
    app: aevatar-agent
  ports:
  - name: http
    port: 80
    targetPort: 8080
  - name: metrics
    port: 9090
    targetPort: 9090
  type: ClusterIP
---
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: aevatar-agent-network-policy
spec:
  podSelector:
    matchLabels:
      app: aevatar-agent
  policyTypes:
  - Ingress
  - Egress
  ingress:
  - from:
    - namespaceSelector:
        matchLabels:
          name: api-gateway
    ports:
    - protocol: TCP
      port: 8080
  egress:
  - to:
    - namespaceSelector:
        matchLabels:
          name: database
    ports:
    - protocol: TCP
      port: 5432
  - to:
    - namespaceSelector:
        matchLabels:
          name: message-broker
    ports:
    - protocol: TCP
      port: 5672
```

### 3. 配置管理

```csharp
// 配置类设计
public class AgentConfiguration
{
    // 使用选项模式
    public AgentOptions Agent { get; set; } = new();
    public RuntimeOptions Runtime { get; set; } = new();
    public AIOptions AI { get; set; } = new();
    public SecurityOptions Security { get; set; } = new();
    public ObservabilityOptions Observability { get; set; } = new();
}

public class AgentOptions
{
    public string Name { get; set; } = "DefaultAgent";
    public string Type { get; set; }
    public int MaxConcurrentEvents { get; set; } = 100;
    public TimeSpan EventTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public Dictionary<string, string> Metadata { get; set; } = new();
}

// 配置验证
public class AgentConfigurationValidator : IValidateOptions<AgentConfiguration>
{
    public ValidateOptionsResult Validate(string name, AgentConfiguration configuration)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(configuration.Agent.Name))
        {
            errors.Add("Agent name is required");
        }

        if (configuration.Agent.MaxConcurrentEvents <= 0)
        {
            errors.Add("MaxConcurrentEvents must be greater than 0");
        }

        if (configuration.Runtime.MaxConcurrentAgents > 10000)
        {
            errors.Add("MaxConcurrentAgents is too high");
        }

        if (errors.Any())
        {
            return ValidateOptionsResult.Fail(errors);
        }

        return ValidateOptionsResult.Success;
    }
}

// 配置绑定
public static class ConfigurationExtensions
{
    public static IServiceCollection ConfigureAgentServices(this IServiceCollection services, IConfiguration configuration)
    {
        // 绑定配置
        services.Configure<AgentConfiguration>(configuration.GetSection("Agent"));

        // 添加配置验证
        services.AddSingleton<IValidateOptions<AgentConfiguration>, AgentConfigurationValidator>();

        // 使用强类型配置
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<AgentConfiguration>>().Value);

        return services;
    }
}
```

## 📈 运维最佳实践

### 1. 健康检查

```csharp
public class AgentHealthCheck : IHealthCheck
{
    private readonly IAgentRuntime _runtime;
    private readonly IMetricsCollector _metrics;
    private readonly ILogger<AgentHealthCheck> _logger;

    public AgentHealthCheck(IAgentRuntime runtime, IMetricsCollector metrics, ILogger<AgentHealthCheck> logger)
    {
        _runtime = runtime;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // ✅ 全面的健康检查

            // 1. 检查运行时状态
            var runtimeStatus = await _runtime.GetStatusAsync(cancellationToken);
            if (runtimeStatus != RuntimeStatus.Running)
            {
                return HealthCheckResult.Unhealthy($"Runtime is not running. Status: {runtimeStatus}");
            }

            // 2. 检查关键组件
            var componentHealth = await CheckComponentsHealthAsync(cancellationToken);
            if (!componentHealth.IsHealthy)
            {
                return HealthCheckResult.Unhealthy($"Component health check failed: {componentHealth.Error}");
            }

            // 3. 检查资源使用
            var resourceHealth = await CheckResourceUsageAsync(cancellationToken);
            if (!resourceHealth.IsHealthy)
            {
                return HealthCheckResult.Degraded($"Resource usage is high: {resourceHealth.Details}");
            }

            // 4. 检查性能指标
            var performanceHealth = await CheckPerformanceMetricsAsync(cancellationToken);
            if (!performanceHealth.IsHealthy)
            {
                return HealthCheckResult.Degraded($"Performance degradation detected: {performanceHealth.Details}");
            }

            // 5. 检查外部依赖
            var dependencyHealth = await CheckExternalDependenciesAsync(cancellationToken);
            if (!dependencyHealth.IsHealthy)
            {
                return HealthCheckResult.Unhealthy($"External dependency check failed: {dependencyHealth.Error}");
            }

            // 6. 构建健康报告
            var healthData = new Dictionary<string, object>
            {
                ["runtime_status"] = runtimeStatus.ToString(),
                ["component_status"] = componentHealth.Status,
                ["resource_usage"] = resourceHealth.Details,
                ["performance_metrics"] = performanceHealth.Metrics,
                ["dependency_status"] = dependencyHealth.Status,
                ["check_time"] = DateTime.UtcNow
            };

            return HealthCheckResult.Healthy("Agent is healthy", healthData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return HealthCheckResult.Unhealthy($"Health check failed: {ex.Message}");
        }
    }

    private async Task<ComponentHealth> CheckComponentsHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 检查事件处理器
            var eventHandlerHealth = await CheckEventHandlersAsync(cancellationToken);

            // 检查工具系统
            var toolSystemHealth = await CheckToolSystemAsync(cancellationToken);

            // 检查内存系统
            var memorySystemHealth = await CheckMemorySystemAsync(cancellationToken);

            var allHealthy = eventHandlerHealth.IsHealthy && toolSystemHealth.IsHealthy && memorySystemHealth.IsHealthy;

            return new ComponentHealth
            {
                IsHealthy = allHealthy,
                Status = new
                {
                    event_handlers = eventHandlerHealth.Status,
                    tool_system = toolSystemHealth.Status,
                    memory_system = memorySystemHealth.Status
                }
            };
        }
        catch (Exception ex)
        {
            return new ComponentHealth { IsHealthy = false, Error = ex.Message };
        }
    }

    private async Task<ResourceHealth> CheckResourceUsageAsync(CancellationToken cancellationToken)
    {
        var process = Process.GetCurrentProcess();

        // 内存使用
        var memoryUsage = process.WorkingSet64 / (1024 * 1024); // MB
        var memoryLimit = 2048; // 2GB limit
        var memoryUsagePercent = (memoryUsage / (double)memoryLimit) * 100;

        // CPU使用
        var cpuUsage = await GetCpuUsageAsync();

        // 线程数
        var threadCount = process.Threads.Count;
        var threadLimit = 100;

        var isHealthy = memoryUsagePercent < 80 && cpuUsage < 80 && threadCount < threadLimit;

        var details = new
        {
            memory_usage_mb = memoryUsage,
            memory_usage_percent = memoryUsagePercent,
            cpu_usage_percent = cpuUsage,
            thread_count = threadCount,
            is_healthy = isHealthy
        };

        return new ResourceHealth
        {
            IsHealthy = isHealthy,
            Details = details
        };
    }
}
```

### 2. 自动扩缩容

```yaml
# horizontal-pod-autoscaler.yaml
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: aevatar-agent-hpa
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: aevatar-agent
  minReplicas: 3
  maxReplicas: 20
  metrics:
  - type: Resource
    resource:
      name: cpu
      target:
        type: Utilization
        averageUtilization: 70
  - type: Resource
    resource:
      name: memory
      target:
        type: Utilization
        averageUtilization: 80
  - type: Pods
    pods:
      metric:
        name: aevatar_events_per_second
      target:
        type: AverageValue
        averageValue: "100"
  - type: External
    external:
      metric:
        name: queue_messages
        selector:
          matchLabels:
            queue: agent-events
      target:
        type: Value
        value: "1000"
  behavior:
    scaleDown:
      stabilizationWindowSeconds: 300
      policies:
      - type: Percent
        value: 10
        periodSeconds: 60
    scaleUp:
      stabilizationWindowSeconds: 60
      policies:
      - type: Percent
        value: 50
        periodSeconds: 60
      - type: Pods
        value: 2
        periodSeconds: 60
      selectPolicy: Max
```

### 3. 备份与恢复

```csharp
public class BackupService
{
    private readonly IEventStore _eventStore;
    private readonly IStateManager _stateManager;
    private readonly ILogger<BackupService> _logger;

    public async Task<BackupResult> CreateBackupAsync(string backupName, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating backup: {BackupName}", backupName);

        try
        {
            var backup = new Backup
            {
                Id = Guid.NewGuid().ToString(),
                Name = backupName,
                CreatedAt = DateTime.UtcNow,
                Status = BackupStatus.InProgress
            };

            // 1. 备份事件存储
            var eventBackup = await BackupEventsAsync(cancellationToken);
            backup.EventBackupPath = eventBackup.Path;
            backup.EventBackupSize = eventBackup.Size;

            // 2. 备份代理状态
            var stateBackup = await BackupStatesAsync(cancellationToken);
            backup.StateBackupPath = stateBackup.Path;
            backup.StateBackupSize = stateBackup.Size;

            // 3. 备份配置
            var configBackup = await BackupConfigurationAsync(cancellationToken);
            backup.ConfigBackupPath = configBackup.Path;

            // 4. 验证备份完整性
            var isValid = await VerifyBackupAsync(backup, cancellationToken);
            backup.Status = isValid ? BackupStatus.Completed : BackupStatus.Failed;

            _logger.LogInformation("Backup {BackupName} completed successfully", backupName);

            return new BackupResult
            {
                Success = isValid,
                Backup = backup,
                Message = isValid ? "Backup completed successfully" : "Backup verification failed"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup {BackupName} failed", backupName);
            return new BackupResult
            {
                Success = false,
                Message = $"Backup failed: {ex.Message}"
            };
        }
    }

    private async Task<BackupFile> BackupEventsAsync(CancellationToken cancellationToken)
    {
        var backupPath = $"/backups/events/{DateTime.UtcNow:yyyyMMddHHmmss}.bak";

        await using var fileStream = new FileStream(backupPath, FileMode.Create, FileAccess.Write);
        await using var writer = new StreamWriter(fileStream);

        // 流式备份事件
        await foreach (var eventEnvelope in _eventStore.GetAllEventsAsync(cancellationToken))
        {
            var json = JsonSerializer.Serialize(eventEnvelope);
            await writer.WriteLineAsync(json);
        }

        return new BackupFile
        {
            Path = backupPath,
            Size = new FileInfo(backupPath).Length
        };
    }

    public async Task<RestoreResult> RestoreFromBackupAsync(string backupId, RestoreOptions options, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting restore from backup: {BackupId}", backupId);

        try
        {
            // 1. 获取备份信息
            var backup = await GetBackupAsync(backupId, cancellationToken);
            if (backup == null)
            {
                return new RestoreResult { Success = false, Message = "Backup not found" };
            }

            // 2. 验证备份完整性
            var isValid = await VerifyBackupAsync(backup, cancellationToken);
            if (!isValid)
            {
                return new RestoreResult { Success = false, Message = "Backup verification failed" };
            }

            // 3. 创建恢复点
            var restorePoint = await CreateRestorePointAsync(cancellationToken);

            // 4. 执行恢复
            if (options.RestoreEvents)
            {
                await RestoreEventsAsync(backup.EventBackupPath, cancellationToken);
            }

            if (options.RestoreStates)
            {
                await RestoreStatesAsync(backup.StateBackupPath, cancellationToken);
            }

            if (options.RestoreConfiguration)
            {
                await RestoreConfigurationAsync(backup.ConfigBackupPath, cancellationToken);
            }

            _logger.LogInformation("Restore from backup {BackupId} completed successfully", backupId);

            return new RestoreResult
            {
                Success = true,
                Message = "Restore completed successfully",
                RestorePoint = restorePoint
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Restore from backup {BackupId} failed", backupId);

            // 回滚到恢复点
            await RollbackToRestorePointAsync(options.RestorePointId, cancellationToken);

            return new RestoreResult
            {
                Success = false,
                Message = $"Restore failed: {ex.Message}"
            };
        }
    }
}
```

## 📚 总结

本最佳实践指南涵盖了Aevatar Agent Framework的各个方面：

### 🔧 开发最佳实践
- **命名规范**：使用清晰、描述性的名称
- **状态设计**：保持简单、专注、可序列化
- **事件设计**：语义明确，包含必要的上下文
- **AI集成**：良好的提示工程，安全的工具使用
- **工具开发**：完善的验证，安全的权限控制

### ⚡ 性能最佳实践
- **事件处理**：并行处理，批处理，异步操作
- **内存优化**：对象池，流式处理，及时清理
- **数据库访问**：编译查询，批量操作，缓存策略

### 🔐 安全最佳实践
- **输入验证**：多层验证，清理用户输入
- **认证授权**：细粒度权限，审计日志
- **数据保护**：加密存储，完整性检查

### 📊 可观测性最佳实践
- **结构化日志**：统一格式，丰富上下文
- **指标收集**：关键指标，标签维度
- **分布式追踪**：跨服务追踪，性能分析

### 🚀 部署运维最佳实践
- **容器化**：安全容器，健康检查
- **编排配置**：自动扩缩容，网络策略
- **备份恢复**：定期备份，完整性验证

遵循这些最佳实践，可以构建出**高性能、高可用、安全可控**的代理应用系统。

---

*本指南为Aevatar Agent Framework的最佳实践总结，建议在实际开发中结合具体场景灵活应用。*