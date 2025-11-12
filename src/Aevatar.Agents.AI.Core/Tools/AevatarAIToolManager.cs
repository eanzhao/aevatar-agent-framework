using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Aevatar.Agents.AI.Abstractions.Tools;

namespace Aevatar.Agents.AI.Core.Tools;

/// <summary>
/// 简化的AI工具管理器实现
/// </summary>
public class AevatarAIToolManager : IAevatarAIToolManager
{
    private readonly ConcurrentDictionary<string, IAevatarAITool> _tools = new();
    private readonly ILogger<AevatarAIToolManager> _logger;

    public AevatarAIToolManager(ILogger<AevatarAIToolManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public Task RegisterAevatarAIToolAsync(IAevatarAITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        if (string.IsNullOrWhiteSpace(tool.Name))
        {
            throw new ArgumentException("Tool name cannot be null or empty", nameof(tool));
        }

        _tools[tool.Name] = tool;
        _logger.LogInformation("Registered AI tool: {ToolName}", tool.Name);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task RegisterAevatarAIToolAsync(
        string name,
        string description,
        Func<AevatarAIToolContext, Dictionary<string, object>, CancellationToken, Task<AevatarAIToolResult>> executeFunc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(executeFunc);

        var tool = new AevatarAIToolDelegate(name, description ?? string.Empty, executeFunc);
        await RegisterAevatarAIToolAsync(tool);
    }

    /// <inheritdoc/>
    public IAevatarAITool GetAevatarAITool(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _tools.TryGetValue(name, out var tool) ? tool : null;
    }

    /// <inheritdoc/>
    public IEnumerable<IAevatarAITool> GetAllAevatarAITools()
    {
        return _tools.Values;
    }

    /// <inheritdoc/>
    public bool AevatarAIToolExists(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _tools.ContainsKey(name);
    }

    /// <inheritdoc/>
    public async Task<AevatarAIToolResult> ExecuteAevatarAIToolAsync(
        string toolName,
        AevatarAIToolContext context,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(context);

        var tool = GetAevatarAITool(toolName);
        if (tool == null)
        {
            _logger.LogWarning("AI Tool '{ToolName}' not found", toolName);
            return AevatarAIToolResult.CreateFailure($"AI Tool '{toolName}' not found");
        }

        try
        {
            _logger.LogDebug("Executing AI tool: {ToolName} for agent: {AgentId}", toolName, context.AgentId);

            var result = await tool.ExecuteAsync(context, parameters ?? new Dictionary<string, object>(), cancellationToken);

            _logger.LogDebug("AI tool executed successfully: {ToolName}, Success: {Success}", toolName, result.Success);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI tool execution failed: {ToolName}", toolName);
            return AevatarAIToolResult.CreateFailure($"Tool execution failed: {ex.Message}");
        }
    }
}

/// <summary>
/// AI工具委托实现
/// </summary>
public class AevatarAIToolDelegate : IAevatarAITool
{
    private readonly Func<AevatarAIToolContext, Dictionary<string, object>, CancellationToken, Task<AevatarAIToolResult>> _executeFunc;

    public string Name { get; }
    public string Description { get; }

    public AevatarAIToolDelegate(
        string name,
        string description,
        Func<AevatarAIToolContext, Dictionary<string, object>, CancellationToken, Task<AevatarAIToolResult>> executeFunc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(executeFunc);

        Name = name;
        Description = description ?? string.Empty;
        _executeFunc = executeFunc;
    }

    public Task<AevatarAIToolResult> ExecuteAsync(
        AevatarAIToolContext context,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default)
    {
        return _executeFunc(context, parameters ?? new Dictionary<string, object>(), cancellationToken);
    }
}