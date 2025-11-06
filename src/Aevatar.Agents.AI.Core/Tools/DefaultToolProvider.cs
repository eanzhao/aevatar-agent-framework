using Aevatar.Agents.AI.Abstractions;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core.Tools;

/// <summary>
/// 默认的工具提供者实现
/// 提供核心工具和自定义工具的统一管理
/// </summary>
public class DefaultToolProvider : IToolProvider
{
    private readonly ILogger<DefaultToolProvider>? _logger;
    private readonly Dictionary<string, AevatarTool> _toolCache = new();

    public DefaultToolProvider(ILogger<DefaultToolProvider>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 获取所有可用的工具
    /// </summary>
    public virtual async Task<IEnumerable<AevatarTool>> GetToolsAsync(ToolContext context)
    {
        _logger?.LogDebug("Getting tools for agent {AgentId}", context.AgentId);

        var tools = new List<AevatarTool>();

        // 添加核心工具（如果需要）
        if (context.IncludeCoreTools)
        {
            tools.AddRange(await GetCoreToolsAsync(context));
        }

        // 添加自定义工具
        tools.AddRange(await GetCustomToolsAsync(context));

        // 按类别过滤（如果指定）
        if (context.Categories?.Any() == true)
        {
            tools = tools.Where(t => context.Categories.Contains(t.Category)).ToList();
        }

        _logger?.LogInformation("Retrieved {Count} tools for agent {AgentId}",
            tools.Count, context.AgentId);

        return tools;
    }

    /// <summary>
    /// 获取特定的工具
    /// </summary>
    public virtual async Task<AevatarTool?> GetToolAsync(string toolName, ToolContext context)
    {
        _logger?.LogDebug("Getting tool {ToolName} for agent {AgentId}",
            toolName, context.AgentId);

        // 先检查缓存
        if (_toolCache.TryGetValue(toolName, out var cachedTool))
        {
            return cachedTool;
        }

        // 从所有工具中查找
        var tools = await GetToolsAsync(context);
        var tool = tools.FirstOrDefault(t => t.Name == toolName);

        if (tool != null)
        {
            _toolCache[toolName] = tool;
        }

        return tool;
    }

    /// <summary>
    /// 获取工具分类
    /// </summary>
    public virtual async Task<IDictionary<ToolCategory, IList<string>>> GetToolCategoriesAsync()
    {
        var categories = new Dictionary<ToolCategory, IList<string>>();

        // 获取所有工具（使用最小上下文）
        var context = new ToolContext { IncludeCoreTools = true };
        var tools = await GetToolsAsync(context);

        foreach (var tool in tools)
        {
            if (!categories.ContainsKey(tool.Category))
            {
                categories[tool.Category] = new List<string>();
            }

            categories[tool.Category].Add(tool.Name);
        }

        return categories;
    }

    /// <summary>
    /// 获取核心工具
    /// </summary>
    protected virtual async Task<IEnumerable<AevatarTool>> GetCoreToolsAsync(ToolContext context)
    {
        var tools = new List<AevatarTool>
        {
            CreateEventPublishTool(context),
            CreateStateQueryTool(context),
            CreateMemorySearchTool(context)
        };

        return await Task.FromResult(tools);
    }

    /// <summary>
    /// 获取自定义工具（子类可重写）
    /// </summary>
    protected virtual Task<IEnumerable<AevatarTool>> GetCustomToolsAsync(ToolContext context)
    {
        return Task.FromResult(Enumerable.Empty<AevatarTool>());
    }

    /// <summary>
    /// 创建事件发布工具
    /// </summary>
    protected virtual AevatarTool CreateEventPublishTool(ToolContext context)
    {
        return new AevatarTool
        {
            Name = "publish_event",
            Description = "Publish an event to the agent stream",
            Category = ToolCategory.Core,
            RequiresInternalAccess = true,
            CanBeOverridden = false,
            Version = "1.0.0",
            Tags = new List<string> { "core", "event", "communication" },
            Parameters = new AevatarAevatarToolParameters
            {
                Items = new Dictionary<string, AevatarToolParameter>
                {
                    ["event_type"] = new()
                    {
                        Type = "string",
                        Required = true,
                        Description = "The type of event to publish"
                    },
                    ["payload"] = new()
                    {
                        Type = "object",
                        Required = true,
                        Description = "The event payload data"
                    },
                    ["direction"] = new()
                    {
                        Type = "string",
                        Enum = new[] { "up", "down", "both" },
                        Description = "The direction to publish the event",
                        DefaultValue = "both"
                    }
                },
                Required = new[] { "event_type", "payload" }
            },
            ExecuteAsync = async (parameters, executionContext, ct) =>
            {
                if (context.PublishEventCallback == null)
                {
                    _logger?.LogWarning("PublishEventCallback not provided, cannot publish event");
                    return new { success = false, error = "Event publishing not available" };
                }

                try
                {
                    var eventType = parameters["event_type"]?.ToString();
                    var payload = parameters["payload"];
                    var direction = parameters.GetValueOrDefault("direction", "both")?.ToString();

                    _logger?.LogInformation("Publishing event {EventType} with direction {Direction}",
                        eventType, direction);

                    // TODO: Create and publish the actual event based on type and payload
                    var eventId = Guid.NewGuid().ToString();

                    return new
                    {
                        success = true,
                        eventId = eventId,
                        eventType = eventType,
                        direction = direction
                    };
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error publishing event");
                    return new { success = false, error = ex.Message };
                }
            }
        };
    }

    /// <summary>
    /// 创建状态查询工具
    /// </summary>
    protected virtual AevatarTool CreateStateQueryTool(ToolContext context)
    {
        return new AevatarTool
        {
            Name = "query_state",
            Description = "Query agent state information",
            Category = ToolCategory.Core,
            RequiresInternalAccess = true,
            CanBeOverridden = true,
            Version = "1.0.0",
            Tags = new List<string> { "core", "state", "query" },
            Parameters = new AevatarAevatarToolParameters
            {
                Items = new Dictionary<string, AevatarToolParameter>
                {
                    ["field"] = new()
                    {
                        Type = "string",
                        Required = true,
                        Description = "The state field to query"
                    },
                    ["path"] = new()
                    {
                        Type = "string",
                        Description = "Optional JSON path for nested fields"
                    }
                }
            },
            ExecuteAsync = async (parameters, executionContext, ct) =>
            {
                if (context.GetStateCallback == null)
                {
                    _logger?.LogWarning("GetStateCallback not provided, cannot query state");
                    return new { success = false, error = "State querying not available" };
                }

                try
                {
                    var field = parameters["field"]?.ToString();
                    var path = parameters.GetValueOrDefault("path")?.ToString();

                    _logger?.LogDebug("Querying state field {Field} with path {Path}", field, path);

                    var state = context.GetStateCallback();

                    if (state == null)
                    {
                        return new { success = false, error = "State is null" };
                    }

                    var fieldValue = GetFieldValue(state, field, path);

                    return new
                    {
                        success = true,
                        field = field,
                        value = fieldValue,
                        stateType = state.GetType().Name
                    };
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error querying state");
                    return new { success = false, error = ex.Message };
                }
            }
        };
    }

    /// <summary>
    /// 创建记忆搜索工具
    /// </summary>
    protected virtual AevatarTool CreateMemorySearchTool(ToolContext context)
    {
        return new AevatarTool
        {
            Name = "search_memory",
            Description = "Search agent memory for relevant information",
            Category = ToolCategory.Memory,
            RequiresInternalAccess = false,
            CanBeOverridden = true,
            Version = "1.0.0",
            Tags = new List<string> { "memory", "search", "recall" },
            Parameters = new AevatarAevatarToolParameters
            {
                Items = new Dictionary<string, AevatarToolParameter>
                {
                    ["query"] = new()
                    {
                        Type = "string",
                        Required = true,
                        Description = "The search query"
                    },
                    ["top_k"] = new()
                    {
                        Type = "integer",
                        DefaultValue = 5,
                        Description = "Number of top results to return"
                    },
                    ["memory_type"] = new()
                    {
                        Type = "string",
                        Enum = new[] { "all", "conversation", "facts", "procedures" },
                        DefaultValue = "all",
                        Description = "Type of memory to search"
                    }
                }
            },
            ExecuteAsync = async (parameters, executionContext, ct) =>
            {
                if (context.Memory == null)
                {
                    _logger?.LogWarning("Memory not provided, cannot search memory");
                    return new { success = false, error = "Memory search not available" };
                }

                try
                {
                    var query = parameters["query"]?.ToString() ?? "";
                    var topK = Convert.ToInt32(parameters.GetValueOrDefault("top_k", 5));
                    var memoryType = parameters.GetValueOrDefault("memory_type", "all")?.ToString();

                    _logger?.LogDebug("Searching memory with query '{Query}', top_k={TopK}, type={Type}",
                        query, topK, memoryType);

                    var results = await context.Memory.RecallAsync(
                        query,
                        new AevatarRecallOptions { TopK = topK },
                        ct);

                    var formattedResults = results.Select(r => new
                    {
                        content = r.Item.Content,
                        score = r.RelevanceScore,
                        metadata = r.Item.Metadata
                    }).ToList();

                    return new
                    {
                        success = true,
                        query = query,
                        resultsCount = formattedResults.Count,
                        results = formattedResults
                    };
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error searching memory");
                    return new { success = false, error = ex.Message };
                }
            }
        };
    }

    /// <summary>
    /// 通过反射获取字段值
    /// </summary>
    private object? GetFieldValue(object obj, string? fieldName, string? path)
    {
        if (string.IsNullOrEmpty(fieldName))
        {
            return obj;
        }

        try
        {
            var type = obj.GetType();

            // 尝试获取属性
            var property = type.GetProperty(fieldName);
            if (property != null)
            {
                var value = property.GetValue(obj);

                // 如果有路径，继续导航
                if (!string.IsNullOrEmpty(path))
                {
                    // TODO: 实现 JSON path 导航逻辑
                    return value;
                }

                return value;
            }

            // 尝试获取字段
            var field = type.GetField(fieldName);
            if (field != null)
            {
                return field.GetValue(obj);
            }

            // 如果是字典类型
            if (obj is IDictionary<string, object> dict)
            {
                return dict.TryGetValue(fieldName, out var value) ? value : null;
            }

            _logger?.LogWarning("Field {FieldName} not found in type {TypeName}", fieldName, type.Name);
            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error getting field value for {FieldName}", fieldName);
            return null;
        }
    }
}
