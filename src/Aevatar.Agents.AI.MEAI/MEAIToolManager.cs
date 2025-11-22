using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.WithTool;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Aevatar.Agents.AI.WithTool.Messages;
using Microsoft.Extensions.AI;

namespace Aevatar.Agents.AI.MEAI;

/// <summary>
/// Microsoft.Extensions.AI工具管理器
/// 桥接Microsoft.Extensions.AI的AITool到框架的IAevatarToolManager
/// </summary>
internal class MEAIToolManager : IAevatarToolManager
{
    private readonly Dictionary<string, ToolDefinition> _toolDefinitions = new();
    
    /// <inheritdoc />
    public Task RegisterToolAsync(
        ToolDefinition tool,
        CancellationToken cancellationToken = default)
    {
        _toolDefinitions[tool.Name] = tool;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ToolDefinition>> GetAvailableToolsAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ToolDefinition>>(_toolDefinitions.Values.ToList());
    }

    public async Task<ToolExecutionResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        ToolExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Execute via ToolDefinition's ExecuteAsync if available
            if (_toolDefinitions.TryGetValue(toolName, out var definition))
            {
                if (definition.ExecuteAsync != null)
                {
                    var result = await definition.ExecuteAsync(parameters, context, cancellationToken);
                    return new ToolExecutionResult
                    {
                        IsSuccess = true,
                        Content = result?.ToString() ?? string.Empty
                    };
                }
            }
            
            return new ToolExecutionResult
            {
                IsSuccess = false,
                Content = $"Tool {toolName} not found or has no ExecuteAsync"
            };
        }
        catch (Exception ex)
        {
            return new ToolExecutionResult
            {
                IsSuccess = false,
                Content = $"Error executing tool {toolName}: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AevatarFunctionDefinition>> GenerateFunctionDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        var functions = new List<AevatarFunctionDefinition>();
        
        foreach (var tool in _toolDefinitions.Values)
        {
            var func = new AevatarFunctionDefinition
            {
                Name = tool.Name,
                Description = tool.Description,
                Parameters = new Dictionary<string, AevatarParameterDefinition>()
            };
            
            // Convert ToolParameters to AevatarParameterDefinition
            if (tool.Parameters?.Items != null)
            {
                foreach (var param in tool.Parameters.Items)
                {
                    func.Parameters[param.Key] = new AevatarParameterDefinition
                    {
                        Type = param.Value.Type ?? "string",
                        Description = param.Value.Description ?? "",
                        Required = param.Value.Required
                    };
                }
            }
            
            functions.Add(func);
        }
        
        return functions;
    }
}
