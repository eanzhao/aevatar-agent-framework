using Aevatar.Agents.AI.Abstractions;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Aevatar.Agents.AI.Core.Strategies;

/// <summary>
/// 标准AI处理策略
/// 使用直接的提示-响应模式，支持函数调用
/// </summary>
public class StandardProcessingStrategy : IAevatarAIProcessingStrategy
{
    public string Name => "Standard Processing";
    
    public AevatarAIProcessingMode Mode => AevatarAIProcessingMode.Standard;
    
    public async Task<string> ProcessAsync(
        AevatarAIContext context,
        AevatarAIEventHandlerAttribute? config,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken = default)
    {
        dependencies.Logger?.LogDebug("Processing with Standard strategy");
        
        // 构建提示词
        var prompt = await BuildPromptAsync(context, config, dependencies, cancellationToken);
        
        // 调用LLM
        var request = new AevatarLLMRequest
        {
            SystemPrompt = dependencies.Configuration.SystemPrompt,
            UserPrompt = prompt,
            Settings = new AevatarLLMSettings
            {
                ModelId = dependencies.Configuration.Model,
                Temperature = dependencies.Configuration.Temperature,
                MaxTokens = dependencies.Configuration.MaxTokens
            }
        };
        
        // 如果有工具，添加函数定义
        if (config?.RequiredTools?.Length > 0)
        {
            // 简化实现：获取所有工具定义
            var functions = await dependencies.ToolManager.GenerateFunctionDefinitionsAsync(cancellationToken);
            // 过滤出需要的工具
            var requiredFunctions = functions
                .Where(f => config.RequiredTools.Contains(f.Name))
                .ToList();
            request.Functions = requiredFunctions;
        }
        
        var response = await dependencies.LLMProvider.GenerateAsync(request, cancellationToken);
        
        // 处理函数调用
        if (response.AevatarFunctionCall != null)
        {
            var result = await HandleFunctionCallAsync(
                response.AevatarFunctionCall,
                dependencies,
                cancellationToken);
            
            // 将结果添加到对话并重新生成 - 简化实现
            await dependencies.Memory.AddMessageAsync(
                "function",
                $"[{response.AevatarFunctionCall.Name}]: {result?.ToString() ?? string.Empty}",
                cancellationToken);
            
            // 递归调用以获取最终响应
            return await ProcessAsync(context, config, dependencies, cancellationToken);
        }
        
        return response.Content;
    }
    
    private async Task<string> BuildPromptAsync(
        AevatarAIContext context,
        AevatarAIEventHandlerAttribute? config,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(config?.PromptTemplate))
        {
            var parameters = new Dictionary<string, object>
            {
                ["question"] = context.Question ?? string.Empty,
                ["context"] = context.WorkingMemory,
                ["state"] = context.AgentState
            };
            
            return await dependencies.PromptManager.FormatPromptAsync(
                config.PromptTemplate,
                parameters,
                cancellationToken);
        }
        
        // 默认提示词
        return context.Question ?? "Process the received event.";
    }
    
    private async Task<object?> HandleFunctionCallAsync(
        AevatarFunctionCall functionCall,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken)
    {
        if (dependencies.ExecuteToolCallback == null)
        {
            dependencies.Logger?.LogWarning("ExecuteToolCallback not provided, skipping function call");
            return null;
        }
        
        var arguments = ParseArguments(functionCall.Arguments);
        return await dependencies.ExecuteToolCallback(
            functionCall.Name,
            arguments,
            cancellationToken);
    }
    
    private Dictionary<string, object> ParseArguments(string argumentsJson)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(argumentsJson)
                   ?? new Dictionary<string, object>();
        }
        catch
        {
            return new Dictionary<string, object>();
        }
    }
}
