using Aevatar.Agents.AI.Abstractions;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core.Tools.CoreTools;

/// <summary>
/// 记忆搜索工具实现
/// 用于搜索Agent记忆中的相关信息
/// </summary>
public class MemorySearchTool : AevatarToolBase
{
    /// <inheritdoc />
    public override string Name => "search_memory";
    
    /// <inheritdoc />
    public override string Description => "Search agent memory for relevant information using semantic search";
    
    /// <inheritdoc />
    public override ToolCategory Category => ToolCategory.Memory;
    
    /// <inheritdoc />
    public override string Version => "1.0.0";
    
    /// <inheritdoc />
    public override IList<string> Tags => new List<string> { "memory", "search", "recall", "semantic" };
    
    /// <inheritdoc />
    protected override bool RequiresInternalAccess() => false;
    
    /// <inheritdoc />
    protected override bool CanBeOverridden() => true;
    
    /// <inheritdoc />
    protected override TimeSpan? GetTimeout() => TimeSpan.FromSeconds(30);
    
    /// <inheritdoc />
    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["query"] = new()
                {
                    Type = "string",
                    Required = true,
                    Description = "The search query for semantic memory search"
                },
                ["top_k"] = new()
                {
                    Type = "integer",
                    DefaultValue = 5,
                    Description = "Number of top results to return (1-20)"
                },
                ["memory_type"] = new()
                {
                    Type = "string",
                    Enum = new[] { "all", "conversation", "facts", "procedures", "events" },
                    DefaultValue = "all",
                    Description = "Type of memory to search"
                },
                ["min_relevance"] = new()
                {
                    Type = "number",
                    DefaultValue = 0.0,
                    Description = "Minimum relevance score (0.0-1.0)"
                }
            },
            Required = new[] { "query" }
        };
    }
    
    /// <inheritdoc />
    public override async Task<object?> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        // 验证参数
        var validation = ValidateParameters(parameters);
        if (!validation.IsValid)
        {
            logger?.LogWarning("Invalid parameters: {Errors}", string.Join(", ", validation.Errors));
            return new { success = false, errors = validation.Errors };
        }
        
        if (context.Memory == null)
        {
            logger?.LogWarning("Memory not provided, cannot search memory");
            return new { success = false, error = "Memory search not available" };
        }

        try
        {
            var query = parameters["query"]?.ToString() ?? "";
            var topK = ParseTopK(parameters.GetValueOrDefault("top_k", 5));
            var memoryType = parameters.GetValueOrDefault("memory_type", "all")?.ToString();
            var minRelevance = ParseMinRelevance(parameters.GetValueOrDefault("min_relevance", 0.0));

            logger?.LogDebug("Searching memory with query '{Query}', top_k={TopK}, type={Type}, min_relevance={MinRelevance}",
                query, topK, memoryType, minRelevance);

            // 执行搜索
            var results = await context.Memory.RecallAsync(
                query,
                new AevatarRecallOptions 
                { 
                    TopK = topK
                },
                cancellationToken);

            // 过滤和格式化结果
            var formattedResults = results
                .Where(r => r.RelevanceScore >= minRelevance)
                .Select(r => new
                {
                    content = r.Item.Content,
                    score = Math.Round(r.RelevanceScore, 4),
                    category = r.Item.Category,
                    metadata = r.Item.Metadata,
                    timestamp = r.Item.Timestamp
                })
                .ToList();

            logger?.LogInformation("Memory search completed. Found {Count} results with relevance >= {MinRelevance}",
                formattedResults.Count, minRelevance);

            return new
            {
                success = true,
                query = query,
                resultsCount = formattedResults.Count,
                results = formattedResults,
                searchParams = new
                {
                    top_k = topK,
                    memory_type = memoryType,
                    min_relevance = minRelevance
                },
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error searching memory");
            return new { success = false, error = ex.Message };
        }
    }
    
    /// <summary>
    /// 解析 top_k 参数
    /// </summary>
    private static int ParseTopK(object? value)
    {
        if (value == null) return 5;
        
        var topK = Convert.ToInt32(value);
        // 限制范围 1-20
        return Math.Max(1, Math.Min(20, topK));
    }
    
    /// <summary>
    /// 解析最小相关性分数
    /// </summary>
    private static double ParseMinRelevance(object? value)
    {
        if (value == null) return 0.0;
        
        var relevance = Convert.ToDouble(value);
        // 限制范围 0.0-1.0
        return Math.Max(0.0, Math.Min(1.0, relevance));
    }
    
    /// <inheritdoc />
    public override ToolParameterValidationResult ValidateParameters(Dictionary<string, object> parameters)
    {
        var result = base.ValidateParameters(parameters);
        
        // 额外的验证
        if (parameters.TryGetValue("top_k", out var topK))
        {
            try
            {
                var k = Convert.ToInt32(topK);
                if (k < 1 || k > 20)
                {
                    result.Warnings.Add($"top_k value {k} is out of recommended range (1-20), will be clamped");
                }
            }
            catch
            {
                result.IsValid = false;
                result.Errors.Add("top_k must be an integer");
            }
        }
        
        if (parameters.TryGetValue("min_relevance", out var minRel))
        {
            try
            {
                var rel = Convert.ToDouble(minRel);
                if (rel < 0.0 || rel > 1.0)
                {
                    result.Warnings.Add($"min_relevance value {rel} is out of range (0.0-1.0), will be clamped");
                }
            }
            catch
            {
                result.IsValid = false;
                result.Errors.Add("min_relevance must be a number between 0.0 and 1.0");
            }
        }
        
        return result;
    }
}
