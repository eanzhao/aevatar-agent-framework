using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.WithProcessStrategy.Messages;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.WithProcessStrategy.Strategies;

/// <summary>
/// 链式思考AI处理策略
/// 通过逐步推理来解决复杂问题
/// </summary>
public class ChainOfThoughtProcessingStrategy : IAevatarAIProcessingStrategy
{
    public string Name => "Chain of Thought Processing";
    
    public string Description => "链式思考策略 - 通过逐步推理来解决复杂问题，适用于需要深度分析的场景";
    
    public AevatarAIProcessingMode Mode => AevatarAIProcessingMode.ChainOfThought;
    
    public bool CanHandle(AevatarAIContext context)
    {
        // 适合处理复杂推理问题
        if (context.Metadata?.ContainsKey("PreferredStrategy") == true)
        {
            var preferred = context.Metadata["PreferredStrategy"]?.ToString();
            return string.Equals(preferred, "ChainOfThought", StringComparison.OrdinalIgnoreCase);
        }
        
        // 检查问题是否需要推理
        var question = context.Question?.ToLower() ?? string.Empty;
        return question.Contains("为什么") || question.Contains("怎么") || 
               question.Contains("分析") || question.Contains("解释") ||
               question.Contains("why") || question.Contains("how") || 
               question.Contains("analyze") || question.Contains("explain");
    }
    
    public double EstimateComplexity(AevatarAIContext context)
    {
        // 链式思考适合中高复杂度问题
        return 0.6;
    }
    
    public bool ValidateRequirements(AevatarAIStrategyDependencies dependencies)
    {
        return dependencies?.LLMProvider != null && dependencies.Configuration != null;
    }
    
    public async Task<string> ProcessAsync(
        AevatarAIContext context,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken = default)
    {
        dependencies.Logger?.LogDebug("Processing with Chain of Thought strategy");
        
        var thoughts = new List<AevatarThoughtStep>();
        var stepNumber = 1;
        var maxSteps = dependencies.Configuration.MaxChainOfAevatarThoughtSteps ?? 5;
        
        while (stepNumber <= maxSteps)
        {
            // 构建思考提示词 - 简化实现
            var thoughtSteps = thoughts.Count > 0 
                ? string.Join("\n", thoughts.Select((t, i) => $"Step {i + 1}: {t}"))
                : "Let's think step by step.";
            var prompt = $"{thoughtSteps}\n\nQuestion: {context.Question}";
            
            // 生成思考步骤
            var response = await dependencies.LLMProvider.GenerateAsync(new AevatarLLMRequest
            {
                SystemPrompt = "You are an AI that thinks step by step to solve problems. Break down your reasoning into clear steps.",
                UserPrompt = prompt,
                Settings = new AevatarLLMSettings
                {
                    ModelId = dependencies.Configuration.Model,
                    Temperature = 0.3 // 降低温度以获得更确定的推理
                }
            }, cancellationToken);
            
            // 解析思考步骤
            var thought = ParseThoughtStep(response.Content, stepNumber);
            thoughts.Add(thought);
            
            // 发布思考步骤事件
            if (dependencies.PublishEventCallback != null)
            {
                await dependencies.PublishEventCallback(new AevatarThoughtStepEvent
                {
                    AgentId = dependencies.AgentId,
                    ThoughtId = Guid.NewGuid().ToString(),
                    StepNumber = stepNumber,
                    ThoughtContent = thought.Thought,
                    Reasoning = thought.Reasoning ?? string.Empty,
                    Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
                });
            }
            
            // 检查是否得出结论
            if (!string.IsNullOrEmpty(thought.Conclusion) && thought.Confidence > 0.8)
            {
                dependencies.Logger?.LogInformation("Chain of thought reached conclusion at step {Step} with confidence {Confidence}", 
                    stepNumber, thought.Confidence);
                return thought.Conclusion;
            }
            
            stepNumber++;
        }
        
        // 总结所有思考步骤
        return await SummarizeThoughtsAsync(thoughts, dependencies, cancellationToken);
    }
    
    /// <summary>
    /// 解析思考步骤
    /// </summary>
    private AevatarThoughtStep ParseThoughtStep(string content, int stepNumber)
    {
        var thought = new AevatarThoughtStep
        {
            StepNumber = stepNumber,
            Thought = content,
            Confidence = 0.5
        };
        
        // 尝试从内容中提取结构化信息
        // 查找关键词来识别推理、结论等
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines)
        {
            var lowerLine = line.ToLower();
            
            if (lowerLine.Contains("reasoning:") || lowerLine.Contains("because:"))
            {
                thought.Reasoning = line.Substring(line.IndexOf(':') + 1).Trim();
            }
            else if (lowerLine.Contains("conclusion:") || lowerLine.Contains("therefore:") || lowerLine.Contains("answer:"))
            {
                thought.Conclusion = line.Substring(line.IndexOf(':') + 1).Trim();
                thought.Confidence = 0.9; // 如果有明确结论，提高置信度
            }
            else if (lowerLine.Contains("confidence:"))
            {
                if (double.TryParse(line.Substring(line.IndexOf(':') + 1).Trim().TrimEnd('%'), out var conf))
                {
                    thought.Confidence = conf > 1 ? conf / 100 : conf;
                }
            }
        }
        
        // 如果没有单独的reasoning，使用整个内容
        if (string.IsNullOrEmpty(thought.Reasoning))
        {
            thought.Reasoning = content;
        }
        
        return thought;
    }
    
    /// <summary>
    /// 总结思考步骤
    /// </summary>
    private async Task<string> SummarizeThoughtsAsync(
        List<AevatarThoughtStep> thoughts,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken)
    {
        if (!thoughts.Any())
        {
            return "No thoughts generated.";
        }
        
        // 构建总结提示
        var thoughtsSummary = string.Join("\n\n", thoughts.Select((t, i) =>
            $"Step {t.StepNumber}: {t.Thought}\n" +
            (string.IsNullOrEmpty(t.Reasoning) ? "" : $"Reasoning: {t.Reasoning}\n") +
            (string.IsNullOrEmpty(t.Conclusion) ? "" : $"Partial conclusion: {t.Conclusion}")));
        
        var prompt = $"Based on the following chain of thought:\n\n{thoughtsSummary}\n\n" +
                    "Provide a comprehensive final answer that synthesizes all the reasoning steps.";
        
        var response = await dependencies.LLMProvider.GenerateAsync(new AevatarLLMRequest
        {
            SystemPrompt = "You are summarizing a chain of thought reasoning process.",
            UserPrompt = prompt,
            Settings = new AevatarLLMSettings
            {
                ModelId = dependencies.Configuration.Model,
                Temperature = 0.3
            }
        }, cancellationToken);
        
        return response.Content;
    }
}
