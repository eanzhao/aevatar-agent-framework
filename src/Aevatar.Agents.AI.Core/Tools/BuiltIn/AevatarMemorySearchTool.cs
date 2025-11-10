using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.AI.Abstractions.Tools;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core.Tools.BuiltIn;

/// <summary>
/// 内存搜索工具 - 内置AI工具（简化实现）
/// </summary>
public class AevatarMemorySearchTool : IAevatarAITool
{
    private readonly ILogger<AevatarMemorySearchTool> _logger;

    public AevatarMemorySearchTool(ILogger<AevatarMemorySearchTool> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => "search_memory";
    public string Description => "Search agent memory for relevant information";

    public async Task<AevatarAIToolResult> ExecuteAsync(
        AevatarAIToolContext context,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = parameters.GetValueOrDefault("query")?.ToString();
            var maxResultsObj = parameters.GetValueOrDefault("maxResults");
            var memoryType = parameters.GetValueOrDefault("memoryType")?.ToString() ?? "all";

            if (string.IsNullOrWhiteSpace(query))
            {
                _logger.LogWarning("Search query is required but not provided");
                return AevatarAIToolResult.CreateFailure("Search query is required");
            }

            // 解析最大结果数
            var maxResults = 10; // 默认值
            if (maxResultsObj != null)
            {
                if (!int.TryParse(maxResultsObj.ToString(), out maxResults) || maxResults <= 0)
                {
                    _logger.LogWarning("Invalid maxResults value: {MaxResults}, using default 10", maxResultsObj);
                    maxResults = 10;
                }
            }

            // 验证内存类型
            var validMemoryTypes = new[] { "all", "working", "conversation", "longterm" };
            if (!validMemoryTypes.Contains(memoryType.ToLower()))
            {
                _logger.LogWarning("Invalid memory type: {MemoryType}, defaulting to 'all'", memoryType);
                memoryType = "all";
            }

            // 模拟内存搜索结果
            var results = SimulateMemorySearch(query, memoryType, maxResults);

            _logger.LogInformation("Memory search completed: {Query} found {Count} results in {MemoryType} memory",
                query, results.Count, memoryType);

            return AevatarAIToolResult.CreateSuccess(new
            {
                results,
                count = results.Count,
                query,
                memoryType
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Memory search failed for query: {Query}", parameters.GetValueOrDefault("query"));
            return AevatarAIToolResult.CreateFailure($"Memory search failed: {ex.Message}");
        }
    }

    private List<MemoryItem> SimulateMemorySearch(string query, string memoryType, int maxResults)
    {
        // 模拟搜索结果
        var allResults = new List<MemoryItem>();

        switch (memoryType.ToLower())
        {
            case "working":
                allResults.AddRange(new[]
                {
                    new MemoryItem { Id = "1", Type = "working", Content = $"Working memory: {query} information", Timestamp = DateTime.UtcNow.AddHours(-1) },
                    new MemoryItem { Id = "2", Type = "working", Content = $"Current task involves {query}", Timestamp = DateTime.UtcNow.AddHours(-2) }
                });
                break;

            case "conversation":
                allResults.AddRange(new[]
                {
                    new MemoryItem { Id = "3", Type = "conversation", Content = $"User: Tell me about {query}\nAssistant: Here's what I know about {query}...", Timestamp = DateTime.UtcNow.AddHours(-3) },
                    new MemoryItem { Id = "4", Type = "conversation", Content = $"User: What is {query}?\nAssistant: {query} is...", Timestamp = DateTime.UtcNow.AddHours(-4) }
                });
                break;

            case "longterm":
                allResults.AddRange(new[]
                {
                    new MemoryItem { Id = "5", Type = "longterm", Content = $"Long-term knowledge about {query}: key concepts and definitions", Timestamp = DateTime.UtcNow.AddDays(-1) },
                    new MemoryItem { Id = "6", Type = "longterm", Content = $"Historical information regarding {query} from previous sessions", Timestamp = DateTime.UtcNow.AddDays(-2) }
                });
                break;

            case "all":
            default:
                // 包含所有类型的结果
                allResults.AddRange(new[]
                {
                    new MemoryItem { Id = "1", Type = "working", Content = $"Working memory: {query} information", Timestamp = DateTime.UtcNow.AddHours(-1) },
                    new MemoryItem { Id = "3", Type = "conversation", Content = $"User: Tell me about {query}\nAssistant: Here's what I know about {query}...", Timestamp = DateTime.UtcNow.AddHours(-3) },
                    new MemoryItem { Id = "5", Type = "longterm", Content = $"Long-term knowledge about {query}: key concepts and definitions", Timestamp = DateTime.UtcNow.AddDays(-1) }
                });
                break;
        }

        // 过滤包含查询词的结果
        var filteredResults = allResults
            .Where(r => r.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(maxResults)
            .ToList();

        return filteredResults;
    }
}

/// <summary>
/// 内存项
/// </summary>
public class MemoryItem
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}