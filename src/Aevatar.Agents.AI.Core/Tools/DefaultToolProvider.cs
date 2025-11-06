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
    private readonly Dictionary<string, ToolDefinition> _toolCache = new();

    public DefaultToolProvider(ILogger<DefaultToolProvider>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 获取所有可用的工具
    /// </summary>
    public virtual async Task<IEnumerable<ToolDefinition>> GetToolsAsync(ToolContext context)
    {
        _logger?.LogDebug("Getting tools for agent {AgentId}", context.AgentId);

        var tools = new List<ToolDefinition>();

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
    public virtual async Task<ToolDefinition?> GetToolAsync(string toolName, ToolContext context)
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
    protected virtual async Task<IEnumerable<ToolDefinition>> GetCoreToolsAsync(ToolContext context)
    {
        var tools = new List<ToolDefinition>
        {
            CoreToolsRegistry.EventPublisher.CreateToolDefinition(context, _logger),
            CoreToolsRegistry.StateQuery.CreateToolDefinition(context, _logger),
            CoreToolsRegistry.MemorySearch.CreateToolDefinition(context, _logger)
        };
        
        return await Task.FromResult(tools);
    }

    /// <summary>
    /// 获取自定义工具（子类可重写）
    /// </summary>
    protected virtual Task<IEnumerable<ToolDefinition>> GetCustomToolsAsync(ToolContext context)
    {
        return Task.FromResult(Enumerable.Empty<ToolDefinition>());
    }
}