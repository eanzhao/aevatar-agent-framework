using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Messages;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Aevatar.Agents.AI.Core.Strategies;

/// <summary>
/// 思维树（Tree of Thoughts）AI处理策略
/// 探索多个思考分支，评估并选择最优路径
/// </summary>
public class TreeOfThoughtsProcessingStrategy : IAevatarAIProcessingStrategy
{
    public string Name => "Tree of Thoughts Processing";
    
    public string Description => "思维树策略 - 通过探索多个思考路径来解决复杂问题，适用于需要全面探索的场景";
    
    public AevatarAIProcessingMode Mode => AevatarAIProcessingMode.TreeOfThoughts;
    
    public bool CanHandle(AevatarAIContext context)
    {
        // 适合极复杂的问题，需要多路径探索
        if (context.Metadata?.ContainsKey("PreferredStrategy") == true)
        {
            var preferred = context.Metadata["PreferredStrategy"]?.ToString();
            return string.Equals(preferred, "TreeOfThoughts", StringComparison.OrdinalIgnoreCase);
        }
        
        // 适合创造性或有多个解决方案的问题
        var question = context.Question?.ToLower() ?? string.Empty;
        return question.Contains("探索") || question.Contains("方案") ||
               question.Contains("可能性") || question.Contains("选项") ||
               question.Contains("explore") || question.Contains("solutions") ||
               question.Contains("possibilities") || question.Contains("options");
    }
    
    public double EstimateComplexity(AevatarAIContext context)
    {
        // 思维树适合高复杂度问题
        return 0.9;
    }
    
    public bool ValidateRequirements(AevatarAIStrategyDependencies dependencies)
    {
        return dependencies?.LLMProvider != null && dependencies.Configuration != null;
    }
    
    public async Task<string> ProcessAsync(
        AevatarAIContext context,
        AevatarAIEventHandlerAttribute? config,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken = default)
    {
        dependencies.Logger?.LogDebug("Processing with Tree of Thoughts strategy");
        
        var maxDepth = dependencies.Configuration.MaxTreeDepth ?? 3;
        var branchingFactor = dependencies.Configuration.TreeBranchingFactor ?? 3;
        var maxNodes = dependencies.Configuration.MaxTreeNodes ?? 20;
        
        // 初始化根节点
        var root = new ThoughtNode
        {
            Id = Guid.NewGuid().ToString(),
            Content = context.Question ?? "Process the event",
            Depth = 0,
            Score = 1.0
        };
        
        // 使用优先队列管理待探索节点（基于评分）
        var frontier = new PriorityQueue<ThoughtNode, double>();
        frontier.Enqueue(root, -root.Score); // 负分用于降序排列
        
        var exploredNodes = new List<ThoughtNode> { root };
        var solutions = new List<ThoughtNode>();
        
        while (frontier.Count > 0 && exploredNodes.Count < maxNodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // 获取最有前途的节点
            var currentNode = frontier.Dequeue();
            
            dependencies.Logger?.LogDebug("ToT: Exploring node at depth {Depth} with score {Score}", 
                currentNode.Depth, currentNode.Score);
            
            // 检查是否是解决方案
            if (await IsSolutionAsync(currentNode, context, dependencies, cancellationToken))
            {
                solutions.Add(currentNode);
                dependencies.Logger?.LogInformation("ToT: Found solution with score {Score}", currentNode.Score);
                
                // 如果找到高质量解决方案，可以提前结束
                if (currentNode.Score > 0.9)
                {
                    break;
                }
                continue;
            }
            
            // 如果未达最大深度，生成子节点
            if (currentNode.Depth < maxDepth)
            {
                var children = await GenerateChildrenAsync(
                    currentNode, 
                    branchingFactor, 
                    context,
                    dependencies, 
                    cancellationToken);
                
                foreach (var child in children)
                {
                    exploredNodes.Add(child);
                    
                    // 评估子节点
                    child.Score = await EvaluateNodeAsync(child, context, dependencies, cancellationToken);
                    
                    // 如果评分足够高，加入探索队列
                    if (child.Score > 0.3) // 阈值过滤低质量分支
                    {
                        frontier.Enqueue(child, -child.Score);
                    }
                }
            }
        }
        
        // 选择最佳解决方案或路径
        if (solutions.Any())
        {
            var bestSolution = solutions.OrderByDescending(s => s.Score).First();
            return await GenerateFinalAnswerFromNodeAsync(bestSolution, dependencies, cancellationToken);
        }
        
        // 如果没有找到明确解决方案，选择最有前途的叶节点
        var bestLeaf = exploredNodes
            .Where(n => n.Children.Count == 0)
            .OrderByDescending(n => n.Score)
            .FirstOrDefault();
        
        if (bestLeaf != null)
        {
            return await GenerateFinalAnswerFromNodeAsync(bestLeaf, dependencies, cancellationToken);
        }
        
        dependencies.Logger?.LogWarning("ToT: No viable solution found");
        return "Unable to find a satisfactory solution through tree exploration.";
    }
    
    /// <summary>
    /// 生成子节点（思考分支）
    /// </summary>
    private async Task<List<ThoughtNode>> GenerateChildrenAsync(
        ThoughtNode parent,
        int count,
        AevatarAIContext context,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken)
    {
        var children = new List<ThoughtNode>();
        
        // 构建生成提示
        var pathToRoot = GetPathToRoot(parent);
        var thoughtChain = string.Join(" -> ", pathToRoot.Select(n => n.Content));
        
        var prompt = $"Original question: {context.Question}\n\n" +
                    $"Current thought path:\n{thoughtChain}\n\n" +
                    $"Generate {count} different next steps or approaches to explore. " +
                    "Each should be distinct and explore a different aspect or approach. " +
                    "Format each as a separate line starting with a number.";
        
        var response = await dependencies.LLMProvider.GenerateAsync(new AevatarLLMRequest
        {
            SystemPrompt = "You are exploring different thought paths to solve a problem. Be creative and thorough.",
            UserPrompt = prompt,
            Settings = new AevatarLLMSettings
            {
                ModelId = dependencies.Configuration.Model,
                Temperature = 0.7 // 较高温度以获得多样性
            }
        }, cancellationToken);
        
        // 解析响应为多个思考
        var thoughts = ParseMultipleThoughts(response.Content, count);
        
        foreach (var thought in thoughts)
        {
            var child = new ThoughtNode
            {
                Id = Guid.NewGuid().ToString(),
                Content = thought,
                Parent = parent,
                Depth = parent.Depth + 1
            };
            
            parent.Children.Add(child);
            children.Add(child);
        }
        
        return children;
    }
    
    /// <summary>
    /// 评估节点质量
    /// </summary>
    private async Task<double> EvaluateNodeAsync(
        ThoughtNode node,
        AevatarAIContext context,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken)
    {
        var pathToRoot = GetPathToRoot(node);
        var thoughtChain = string.Join(" -> ", pathToRoot.Select(n => n.Content));
        
        var prompt = $"Question: {context.Question}\n\n" +
                    $"Thought path:\n{thoughtChain}\n\n" +
                    "Evaluate this thought path on a scale of 0 to 1:\n" +
                    "- How relevant is it to answering the question?\n" +
                    "- How logical and coherent is the reasoning?\n" +
                    "- How promising is this direction?\n" +
                    "Respond with just a number between 0 and 1.";
        
        var response = await dependencies.LLMProvider.GenerateAsync(new AevatarLLMRequest
        {
            SystemPrompt = "You are evaluating the quality of a reasoning path. Be objective and critical.",
            UserPrompt = prompt,
            Settings = new AevatarLLMSettings
            {
                ModelId = dependencies.Configuration.Model,
                Temperature = 0.1,
                MaxTokens = 10
            }
        }, cancellationToken);
        
        if (double.TryParse(response.Content.Trim(), out var score))
        {
            return Math.Max(0, Math.Min(1, score)); // Clamp to [0, 1]
        }
        
        return 0.5; // Default score if parsing fails
    }
    
    /// <summary>
    /// 检查节点是否是解决方案
    /// </summary>
    private async Task<bool> IsSolutionAsync(
        ThoughtNode node,
        AevatarAIContext context,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken)
    {
        // 根节点不是解决方案
        if (node.Depth == 0)
        {
            return false;
        }
        
        var pathToRoot = GetPathToRoot(node);
        var thoughtChain = string.Join(" -> ", pathToRoot.Select(n => n.Content));
        
        var prompt = $"Question: {context.Question}\n\n" +
                    $"Thought path:\n{thoughtChain}\n\n" +
                    "Does this thought path contain a complete answer to the question? " +
                    "Respond with YES or NO.";
        
        var response = await dependencies.LLMProvider.GenerateAsync(new AevatarLLMRequest
        {
            SystemPrompt = "Determine if a reasoning path provides a complete answer.",
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
    /// 从节点生成最终答案
    /// </summary>
    private async Task<string> GenerateFinalAnswerFromNodeAsync(
        ThoughtNode node,
        AevatarAIStrategyDependencies dependencies,
        CancellationToken cancellationToken)
    {
        var pathToRoot = GetPathToRoot(node);
        var thoughtChain = string.Join("\n", pathToRoot.Select((n, i) => 
            $"{new string(' ', i * 2)}Step {i + 1}: {n.Content}"));
        
        var prompt = $"Based on this reasoning path:\n{thoughtChain}\n\n" +
                    "Provide a clear, comprehensive final answer that synthesizes all the insights from this thought process.";
        
        var response = await dependencies.LLMProvider.GenerateAsync(new AevatarLLMRequest
        {
            SystemPrompt = "Synthesize the reasoning path into a final answer.",
            UserPrompt = prompt,
            Settings = new AevatarLLMSettings
            {
                ModelId = dependencies.Configuration.Model,
                Temperature = 0.3
            }
        }, cancellationToken);
        
        // 发布思维树完成事件
        if (dependencies.PublishEventCallback != null)
        {
            await dependencies.PublishEventCallback(new AevatarTreeOfThoughtsCompletedEvent
            {
                AgentId = dependencies.AgentId,
                ThoughtTreeId = Guid.NewGuid().ToString(),
                SelectedPath = thoughtChain,
                Confidence = node.Score,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            });
        }
        
        return response.Content;
    }
    
    /// <summary>
    /// 获取从节点到根的路径
    /// </summary>
    private List<ThoughtNode> GetPathToRoot(ThoughtNode node)
    {
        var path = new List<ThoughtNode>();
        var current = node;
        
        while (current != null)
        {
            path.Insert(0, current); // 插入到开头以保持从根到叶的顺序
            current = current.Parent;
        }
        
        return path;
    }
    
    /// <summary>
    /// 解析多个思考
    /// </summary>
    private List<string> ParseMultipleThoughts(string content, int expectedCount)
    {
        var thoughts = new List<string>();
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            
            // 移除数字前缀（如 "1. ", "2) ", etc.）
            var cleaned = System.Text.RegularExpressions.Regex.Replace(
                trimmed, @"^\d+[\.\)]\s*", "");
            
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                thoughts.Add(cleaned);
            }
            
            if (thoughts.Count >= expectedCount)
            {
                break;
            }
        }
        
        // 如果没有足够的思考，添加默认的
        while (thoughts.Count < expectedCount)
        {
            thoughts.Add($"Alternative approach {thoughts.Count + 1}");
        }
        
        return thoughts;
    }
}

/// <summary>
/// 思维树节点
/// </summary>
internal class ThoughtNode
{
    public string Id { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public ThoughtNode? Parent { get; set; }
    public List<ThoughtNode> Children { get; set; } = new();
    public int Depth { get; set; }
    public double Score { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}
