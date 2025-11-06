using Aevatar.Agents.AI.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Aevatar.Agents.AI.Core.Tools;

/// <summary>
/// 默认的工具执行器实现
/// 负责工具的执行、监控和事件发布
/// </summary>
public class DefaultToolExecutor : IToolExecutor
{
    private readonly ILogger<DefaultToolExecutor>? _logger;
    
    public DefaultToolExecutor(ILogger<DefaultToolExecutor>? logger = null)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// 执行工具并发布相关事件
    /// </summary>
    public virtual async Task<ToolExecutionResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var executionId = Guid.NewGuid().ToString();
        
        _logger?.LogInformation("Executing tool {ToolName} with execution ID {ExecutionId}", 
            toolName, executionId);
        
        try
        {
            // 验证上下文
            ValidateContext(context);
            
            // 执行工具
            var result = await context.ToolManager.ExecuteToolAsync(
                toolName,
                parameters,
                new AevatarExecutionContext
                {
                    AgentId = Guid.TryParse(context.AgentId, out var agentGuid) ? agentGuid : (Guid?)null,
                    SessionId = context.GetSessionId()
                },
                cancellationToken);
            
            stopwatch.Stop();
            
            // 创建执行结果
            var executionResult = new ToolExecutionResult
            {
                ExecutionId = executionId,
                ToolName = toolName,
                Success = result.Success,
                Result = result.Result,
                Error = result.Error,
                ExecutionTime = stopwatch.Elapsed,
                Metadata = new Dictionary<string, object>
                {
                    ["parameters"] = parameters,
                    ["agentId"] = context.AgentId,
                    ["sessionId"] = context.GetSessionId()
                }
            };
            
            // 发布工具执行事件
            await PublishToolExecutedEventAsync(executionResult, context, cancellationToken);
            
            // 记录到内存（如果配置了）
            if (context.RecordToMemory && context.Memory != null)
            {
                await RecordExecutionToMemoryAsync(executionResult, context, cancellationToken);
            }
            
            return executionResult;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            _logger?.LogError(ex, "Error executing tool {ToolName}", toolName);
            
            var errorResult = new ToolExecutionResult
            {
                ExecutionId = executionId,
                ToolName = toolName,
                Success = false,
                Error = ex.Message,
                ExecutionTime = stopwatch.Elapsed
            };
            
            // 发布错误事件
            await PublishToolErrorEventAsync(errorResult, ex, context, cancellationToken);
            
            return errorResult;
        }
    }
    
    /// <summary>
    /// 批量执行工具
    /// </summary>
    public virtual async Task<IEnumerable<ToolExecutionResult>> ExecuteToolsAsync(
        IEnumerable<ToolExecutionRequest> requests,
        ToolExecutionContext context,
        bool parallel = false,
        CancellationToken cancellationToken = default)
    {
        if (parallel)
        {
            var tasks = requests.Select(r => 
                ExecuteToolAsync(r.ToolName, r.Parameters, context, cancellationToken));
            return await Task.WhenAll(tasks);
        }

        var results = new List<ToolExecutionResult>();
        foreach (var request in requests)
        {
            var result = await ExecuteToolAsync(
                request.ToolName, 
                request.Parameters, 
                context, 
                cancellationToken);
            results.Add(result);
        }
        return results;
    }
    
    /// <summary>
    /// 验证执行上下文
    /// </summary>
    protected virtual void ValidateContext(ToolExecutionContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }
        
        if (context.ToolManager == null)
        {
            throw new InvalidOperationException("ToolManager is required in ToolExecutionContext");
        }
        
        if (string.IsNullOrEmpty(context.AgentId))
        {
            throw new InvalidOperationException("AgentId is required in ToolExecutionContext");
        }
    }
    
    /// <summary>
    /// 发布工具执行成功事件
    /// </summary>
    protected virtual async Task PublishToolExecutedEventAsync(
        ToolExecutionResult result,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (context.PublishEventCallback == null)
        {
            _logger?.LogDebug("PublishEventCallback not provided, skipping event publication");
            return;
        }
        
        var toolEvent = new AevatarToolExecutedEvent
        {
            ToolName = result.ToolName,
            Result = result.Result?.ToString() ?? string.Empty,
            Success = result.Success,
            DurationMs = (long)result.ExecutionTime.TotalMilliseconds
        };
        
        // 添加参数到 map
        if (result.Metadata?.TryGetValue("parameters", out var parameters) == true && 
            parameters is Dictionary<string, object> paramDict)
        {
            foreach (var param in paramDict)
            {
                toolEvent.Parameters[param.Key] = param.Value?.ToString() ?? string.Empty;
            }
        }
        
        await context.PublishEventCallback(toolEvent);
        
        _logger?.LogDebug("Published tool executed event for {ToolName}", result.ToolName);
    }
    
    /// <summary>
    /// 发布工具执行错误事件
    /// </summary>
    protected virtual async Task PublishToolErrorEventAsync(
        ToolExecutionResult result,
        Exception exception,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (context.PublishEventCallback == null)
        {
            return;
        }
        
        var errorEvent = new AevatarAIErrorEvent
        {
            ErrorType = "ToolExecutionError",
            Message = $"Tool '{result.ToolName}' execution failed: {exception.Message}",
            StackTrace = exception.StackTrace ?? string.Empty,
            Context = $"ExecutionId: {result.ExecutionId}",
            Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
        };
        
        await context.PublishEventCallback(errorEvent);
        
        _logger?.LogDebug("Published tool error event for {ToolName}", result.ToolName);
    }
    
    /// <summary>
    /// 记录执行到记忆
    /// </summary>
    protected virtual async Task RecordExecutionToMemoryAsync(
        ToolExecutionResult result,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (context.Memory == null)
        {
            return;
        }
        
        try
        {
            var memoryContent = $"Tool '{result.ToolName}' executed " +
                              $"{(result.Success ? "successfully" : "with error")}. " +
                              $"Result: {result.Result?.ToString() ?? result.Error ?? "N/A"}";
            
            await context.Memory.StoreMemoryAsync(
                new AevatarMemoryItem
                {
                    Content = memoryContent,
                    Category = "ToolExecution",
                    Metadata = new Dictionary<string, object>
                    {
                        ["toolName"] = result.ToolName,
                        ["executionId"] = result.ExecutionId,
                        ["success"] = result.Success,
                        ["duration_ms"] = result.ExecutionTime.TotalMilliseconds
                    }
                },
                cancellationToken);
            
            _logger?.LogDebug("Recorded tool execution to memory for {ToolName}", result.ToolName);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to record tool execution to memory");
        }
    }
}