using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core.Observability;

/// <summary>
/// 日志作用域辅助类
/// 提供结构化日志支持
/// </summary>
public static class LoggingScope
{
    /// <summary>
    /// 创建 Agent 操作的日志作用域
    /// </summary>
    public static IDisposable CreateAgentScope(
        ILogger logger,
        Guid agentId,
        string operation,
        Dictionary<string, object>? additionalData = null)
    {
        var scopeData = new Dictionary<string, object>
        {
            ["AgentId"] = agentId,
            ["Operation"] = operation
        };
        
        if (additionalData != null)
        {
            foreach (var kvp in additionalData)
            {
                scopeData[kvp.Key] = kvp.Value;
            }
        }
        
        return logger.BeginScope(scopeData) ?? new NoOpDisposable();
    }
    
    /// <summary>
    /// 创建事件处理的日志作用域
    /// </summary>
    public static IDisposable CreateEventHandlingScope(
        ILogger logger,
        Guid agentId,
        string eventId,
        string eventType,
        string? correlationId = null)
    {
        var scopeData = new Dictionary<string, object>
        {
            ["AgentId"] = agentId,
            ["EventId"] = eventId,
            ["EventType"] = eventType
        };
        
        if (correlationId != null)
        {
            scopeData["CorrelationId"] = correlationId;
        }
        
        return logger.BeginScope(scopeData) ?? new NoOpDisposable();
    }
    
    /// <summary>
    /// 空的 Disposable（当 BeginScope 返回 null 时使用）
    /// </summary>
    private class NoOpDisposable : IDisposable
    {
        public void Dispose() { }
    }
}

