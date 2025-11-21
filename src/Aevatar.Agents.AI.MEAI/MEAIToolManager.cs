using Aevatar.Agents.AI.Abstractions;
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
    private readonly Dictionary<string, AITool> _aiTools = new();
    private readonly Dictionary<string, ToolDefinition> _toolDefinitions = new();
    
    /// <summary>
    /// 注册Microsoft.Extensions.AI工具
    /// </summary>
    public void RegisterAITool(AITool aiTool)
    {
        if (aiTool != null)
        {
            var name = $"Tool_{_aiTools.Count}";
            _aiTools[name] = aiTool;
            
            // Create corresponding ToolDefinition
            var definition = new ToolDefinition
            {
                Name = name,
                Description = "AI Tool",
                Parameters = new ToolParameters()
            };
            _toolDefinitions[name] = definition;
        }
    }
    
    /// <inheritdoc />
    public Task RegisterToolAsync(
        ToolDefinition tool,
        CancellationToken cancellationToken = default)
    {
        _toolDefinitions[tool.Name] = tool;
        
        // Try to create a corresponding AITool if possible
        // This is a simplified approach - real implementation might need more logic
        Func<Dictionary<string, object?>, Task<object>> handler = async (args) =>
        {
            return $"Tool {tool.Name} executed";
        };
        
        var aiTool = AIFunctionFactory.Create(handler);
        _aiTools[tool.Name] = aiTool;
        
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
    
    /// <summary>
    /// 获取所有注册的AITools
    /// </summary>
    public IReadOnlyList<AITool> GetAITools()
    {
        return _aiTools.Values.ToList();
    }
    
    private ToolParameters ConvertToToolParameters(AITool aiTool)
    {
        // Simplified conversion since AITool doesn't have Metadata property
        return new ToolParameters();
    }
}
