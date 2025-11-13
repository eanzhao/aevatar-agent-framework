using Aevatar.Agents.AI.Abstractions;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Aevatar.Agents.AI.Core.Strategies;

/// <summary>
/// ReAct（Reasoning + Acting）AI处理策略
/// 交替进行推理和行动，通过观察反馈来迭代解决问题
/// </summary>
public class ReActProcessingStrategy : IAevatarAIProcessingStrategy
{
    public string Name => "ReAct Processing";
    
    public string Description => "ReAct策略 - 结合推理和行动，通过交替思考和执行工具来解决问题";
    
    public AevatarAIProcessingMode Mode => AevatarAIProcessingMode.ReAct;
    
    public bool CanHandle(AevatarAIContext context)
    {
        // 适合需要思考和行动结合的场景
        if (context.Metadata?.ContainsKey("PreferredStrategy") == true)
        {
            var preferred = context.Metadata["PreferredStrategy"]?.ToString();
            return string.Equals(preferred, "ReAct", StringComparison.OrdinalIgnoreCase);
        }
        
        // 适合需要多步骤操作或工具交互的场景
        return context.Metadata?.ContainsKey("RequiresMultipleTools") == true;
    }
    
    public double EstimateComplexity(AevatarAIContext context)
    {
        // ReAct适合中高复杂度，特别是需要工具交互的场景
        return 0.7;
    }
    
    public bool ValidateRequirements(AevatarAIStrategyDependencies dependencies)
    {
        // ReAct需要工具管理器
        return dependencies?.LLMProvider != null && 
               dependencies.Configuration != null &&
               dependencies.ToolManager != null;
    }
    
    public async Task<string> ProcessAsync(
        AevatarAIContext context,
        AevatarAIEventHandlerAttribute? config,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken = default)
    {
        dependencies.Logger?.LogDebug("Processing with ReAct strategy");
        
        var maxIterations = dependencies.Configuration.MaxReActIterations ?? 10;
        var iteration = 0;
        var observations = new List<ReActObservation>();
        
        while (iteration < maxIterations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // Step 1: Thought - 推理下一步
            var thought = await GenerateThoughtAsync(context, observations, dependencies, cancellationToken);
            
            if (string.IsNullOrEmpty(thought))
            {
                dependencies.Logger?.LogWarning("ReAct: Empty thought generated at iteration {Iteration}", iteration);
                break;
            }
            
            dependencies.Logger?.LogDebug("ReAct Thought [{Iteration}]: {Thought}", iteration, thought);
            
            // Step 2: Action - 决定动作
            var action = await DetermineActionAsync(thought, dependencies, cancellationToken);
            
            if (action == null)
            {
                dependencies.Logger?.LogDebug("ReAct: No action determined, ending iteration");
                break;
            }
            
            dependencies.Logger?.LogDebug("ReAct Action [{Iteration}]: {Action}", iteration, action.Name);
            
            // Check if task is complete
            if (action.IsFinish)
            {
                dependencies.Logger?.LogInformation("ReAct: Task completed at iteration {Iteration}", iteration);
                return action.Result ?? thought;
            }
            
            // Step 3: Observation - 执行动作并观察结果
            var observation = await ExecuteActionAndObserveAsync(action, dependencies, cancellationToken);
            observations.Add(observation);
            
            dependencies.Logger?.LogDebug("ReAct Observation [{Iteration}]: {Observation}", 
                iteration, observation.Content);
            
            // Check if task is complete based on observations
            if (await IsTaskCompleteAsync(context, observations, dependencies, cancellationToken))
            {
                dependencies.Logger?.LogInformation("ReAct: Task determined complete at iteration {Iteration}", iteration);
                return await GenerateFinalAnswerAsync(context, observations, dependencies, cancellationToken);
            }
            
            iteration++;
        }
        
        dependencies.Logger?.LogWarning("ReAct: Max iterations ({MaxIterations}) reached", maxIterations);
        return await GenerateFinalAnswerAsync(context, observations, dependencies, cancellationToken);
    }
    
    /// <summary>
    /// 生成推理思考
    /// </summary>
    private async Task<string> GenerateThoughtAsync(
        AevatarAIContext context,
        List<ReActObservation> observations,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken)
    {
        var prompt = $"Question: {context.Question}\n\n";
        
        if (observations.Any())
        {
            prompt += "Previous observations:\n";
            foreach (var obs in observations)
            {
                prompt += $"- Action: {obs.Action}\n";
                prompt += $"  Result: {obs.Content}\n";
            }
            prompt += "\n";
        }
        
        prompt += "Based on the question and any previous observations, what should I think about next? ";
        prompt += "Analyze what information we have and what we still need.";
        
        var response = await dependencies.LLMProvider.GenerateAsync(new AevatarLLMRequest
        {
            SystemPrompt = "You are using the ReAct pattern. First think about the problem, then decide on an action, then observe the result. Be analytical and systematic.",
            UserPrompt = prompt,
            Settings = new AevatarLLMSettings
            {
                ModelId = dependencies.Configuration.Model,
                Temperature = 0.5
            }
        }, cancellationToken);
        
        return response.Content;
    }
    
    /// <summary>
    /// 基于思考决定行动
    /// </summary>
    private async Task<ReActAction?> DetermineActionAsync(
        string thought,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken)
    {
        var availableTools = await dependencies.ToolManager.GetAvailableToolsAsync(cancellationToken);
        var toolNames = availableTools.Select(t => t.Name).ToList();
        
        var prompt = $"Based on this thought:\n{thought}\n\n";
        prompt += "Available actions:\n";
        foreach (var tool in availableTools)
        {
            prompt += $"- {tool.Name}: {tool.Description}\n";
        }
        prompt += "- FINISH: If you have enough information to answer the question\n\n";
        prompt += "What action should I take? Respond with either:\n";
        prompt += "1. A function call with the tool name and parameters\n";
        prompt += "2. FINISH: <final answer> if you're ready to provide the final answer";
        
        var functions = await dependencies.ToolManager.GenerateFunctionDefinitionsAsync(
            cancellationToken: cancellationToken);
        
        var response = await dependencies.LLMProvider.GenerateAsync(new AevatarLLMRequest
        {
            SystemPrompt = "Select the most appropriate action based on the current thought.",
            UserPrompt = prompt,
            Functions = functions.ToList(),
            Settings = new AevatarLLMSettings
            {
                ModelId = dependencies.Configuration.Model,
                Temperature = 0.3
            }
        }, cancellationToken);
        
        // Check for function call
        if (response.AevatarFunctionCall != null)
        {
            return new ReActAction
            {
                Name = response.AevatarFunctionCall.Name,
                Parameters = ParseArguments(response.AevatarFunctionCall.Arguments),
                IsFinish = false
            };
        }
        
        // Check for FINISH
        var content = response.Content.Trim();
        if (content.StartsWith("FINISH:", StringComparison.OrdinalIgnoreCase))
        {
            return new ReActAction
            {
                Name = "FINISH",
                IsFinish = true,
                Result = content.Substring(7).Trim()
            };
        }
        
        // Try to parse as action
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            foreach (var toolName in toolNames)
            {
                if (line.Contains(toolName, StringComparison.OrdinalIgnoreCase))
                {
                    return new ReActAction
                    {
                        Name = toolName,
                        Parameters = new Dictionary<string, object>(),
                        IsFinish = false
                    };
                }
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// 执行动作并观察结果
    /// </summary>
    private async Task<ReActObservation> ExecuteActionAndObserveAsync(
        ReActAction action,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken)
    {
        if (dependencies.ExecuteToolCallback == null)
        {
            return new ReActObservation
            {
                Action = action.Name,
                Content = "Tool execution not available",
                Success = false
            };
        }
        
        try
        {
            var result = await dependencies.ExecuteToolCallback(
                action.Name,
                action.Parameters,
                cancellationToken);
            
            return new ReActObservation
            {
                Action = action.Name,
                Content = result?.ToString() ?? "No result",
                Success = true,
                Result = result
            };
        }
        catch (Exception ex)
        {
            dependencies.Logger?.LogError(ex, "Error executing action {Action}", action.Name);
            return new ReActObservation
            {
                Action = action.Name,
                Content = $"Error: {ex.Message}",
                Success = false
            };
        }
    }
    
    /// <summary>
    /// 判断任务是否完成
    /// </summary>
    private async Task<bool> IsTaskCompleteAsync(
        AevatarAIContext context,
        List<ReActObservation> observations,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken)
    {
        // 简单的启发式：如果有足够的成功观察，可能已完成
        var successfulObservations = observations.Count(o => o.Success);
        if (successfulObservations < 2)
        {
            return false;
        }
        
        // 使用LLM判断是否有足够信息回答问题
        var observationsSummary = string.Join("\n", observations.Select(o => 
            $"- {o.Action}: {o.Content}"));
        
        var prompt = $"Question: {context.Question}\n\n" +
                    $"Observations collected:\n{observationsSummary}\n\n" +
                    "Do we have enough information to answer the question? Respond with YES or NO.";
        
        var response = await dependencies.LLMProvider.GenerateAsync(new AevatarLLMRequest
        {
            UserPrompt = prompt,
            Settings = new AevatarLLMSettings
            {
                ModelId = dependencies.Configuration.Model,
                Temperature = 0.1,
                MaxTokens = 10
            }
        }, cancellationToken);
        
        return response.Content.Contains("YES", StringComparison.OrdinalIgnoreCase);
    }
    
    /// <summary>
    /// 生成最终答案
    /// </summary>
    private async Task<string> GenerateFinalAnswerAsync(
        AevatarAIContext context,
        List<ReActObservation> observations,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken)
    {
        var observationsSummary = string.Join("\n", observations.Select(o => 
            $"- Action: {o.Action}\n  Result: {o.Content}"));
        
        var prompt = $"Based on the question: {context.Question}\n\n" +
                    $"And these observations from actions taken:\n{observationsSummary}\n\n" +
                    "Provide a comprehensive final answer that directly addresses the original question.";
        
        var response = await dependencies.LLMProvider.GenerateAsync(new AevatarLLMRequest
        {
            SystemPrompt = "Synthesize the observations into a clear, concise answer.",
            UserPrompt = prompt,
            Settings = new AevatarLLMSettings
            {
                ModelId = dependencies.Configuration.Model,
                Temperature = 0.3
            }
        }, cancellationToken);
        
        return response.Content;
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

/// <summary>
/// ReAct动作
/// </summary>
internal class ReActAction
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
    public bool IsFinish { get; set; }
    public string? Result { get; set; }
}

/// <summary>
/// ReAct观察结果
/// </summary>
internal class ReActObservation
{
    public string Action { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool Success { get; set; }
    public object? Result { get; set; }
}
