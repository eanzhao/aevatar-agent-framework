using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Cognitive.Engine;
using Aevatar.Agents.Cognitive.Messages;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Template;
using Aevatar.Agents.Maker;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

using WorkflowDefinition = Aevatar.Agents.Cognitive.Primitives.WorkflowDefinition;
using StepDefinition = Aevatar.Agents.Cognitive.Primitives.StepDefinition;
using VoteResult = Aevatar.Agents.Maker.VoteResult;

namespace Aevatar.Agents.Cognitive.Agents;

// ============================================================
//  Cognitive Coordinator Agent
//  DSL 工作流协调器 - 真正的分布式并行
// ============================================================

/// <summary>
/// Cognitive Coordinator Agent - 工作流协调器
/// 
/// 并行模型：
/// - 简单步骤（单次 LLM）：Coordinator 直接执行
/// - 并行步骤（fan_out）：通过 Protobuf 事件分发给 Worker Actors
/// 
/// 这遵循 MAKER 的设计模式：
/// - MakerCoordinatorGAgent → CognitiveCoordinatorGAgent
/// - MakerWorkerGAgent → CognitiveWorkerGAgent
/// </summary>
public class CognitiveCoordinatorGAgent : AIGAgentBase<CognitiveCoordinatorState>
{
    // ============================================================
    //  组件
    // ============================================================
    
    private readonly TemplateEngine _templateEngine = new();
    private readonly OutputParserFactory _parserFactory = new();
    private readonly InMemoryWorkflowRegistry _workflowRegistry = new();
    
    // ============================================================
    //  运行时状态
    // ============================================================
    
    private readonly Dictionary<string, object> _workflowVariables = new();
    
    // Fan-out 结果收集
    private readonly ConcurrentDictionary<string, StepCompletedEventProto> _collectedResults = new();
    private int _expectedResults;
    private TaskCompletionSource<bool>? _fanOutCompletionSource;
    
    // Worker 管理（由外部注入）
    private IGAgentActorManager? _actorManager;
    private readonly List<Guid> _workerIds = [];
    
    // 语义聚类投票 (可选)
    private IEmbeddingGenerator<string, Embedding<float>>? _embeddingGenerator;
    private float _semanticSimilarityThreshold = 0.85f;
    
    // ============================================================
    //  构造函数
    // ============================================================
    
    public CognitiveCoordinatorGAgent() { }
    public CognitiveCoordinatorGAgent(Guid id) : base(id) { }
    
    // ============================================================
    //  生命周期
    // ============================================================
    
    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        
        CustomState.MaxDepth = 10;
        CustomState.Status = ExecutionStatus.EsPending;
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult(
            $"CognitiveCoordinator [{CustomState.ExecutionId}] - " +
            $"Phase: {CustomState.CurrentPhase}, Workers: {_workerIds.Count}");
    }
    
    // ============================================================
    //  公共 API
    // ============================================================
    
    /// <summary>
    /// 设置 Actor Manager（用于创建 Workers）
    /// </summary>
    public void SetActorManager(IGAgentActorManager actorManager)
    {
        _actorManager = actorManager;
    }
    
    /// <summary>
    /// 设置 Embedding Generator（用于语义聚类投票）
    /// 如果不设置，投票将使用精确哈希匹配
    /// </summary>
    public void SetEmbeddingGenerator(
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator,
        float semanticSimilarityThreshold = 0.85f)
    {
        _embeddingGenerator = embeddingGenerator;
        _semanticSimilarityThreshold = Math.Clamp(semanticSimilarityThreshold, 0.1f, 1.0f);
        
        Logger.LogInformation(
            "Embedding generator configured: {Enabled}, similarity threshold: {Threshold:F2}",
            embeddingGenerator != null, _semanticSimilarityThreshold);
    }
    
    /// <summary>
    /// 创建 Worker 池
    /// </summary>
    public async Task CreateWorkerPoolAsync(int poolSize = 5)
    {
        if (_actorManager == null)
        {
            throw new InvalidOperationException("ActorManager not set. Call SetActorManager first.");
        }
        
        _workerIds.Clear();
        
        for (int i = 0; i < poolSize; i++)
        {
            // 创建 Worker Actor
            var workerId = Guid.NewGuid();
            await _actorManager.CreateAndRegisterAsync<CognitiveWorkerGAgent>(workerId);
            
            // 设置父子关系（Worker 订阅 Coordinator 的流）
            await _actorManager.LinkParentChildAsync(Id, workerId);
            
            _workerIds.Add(workerId);
        }
        
        CustomState.ActiveWorkers = _workerIds.Count;
        
        Logger.LogInformation("Created worker pool with {Count} workers", poolSize);
    }
    
    /// <summary>
    /// 注册工作流
    /// </summary>
    public void RegisterWorkflow(WorkflowDefinition workflow)
    {
        _workflowRegistry.Register(workflow);
    }
    
    /// <summary>
    /// 获取执行结果
    /// </summary>
    public WorkflowResult GetResult() => new()
    {
        Success = CustomState.Status == ExecutionStatus.EsCompleted,
        Output = _workflowVariables.GetValueOrDefault("_output"),
        TotalTokens = CustomState.TotalTokensUsed,
        TotalLlmCalls = CustomState.TotalLlmCalls
    };
    
    // ============================================================
    //  事件处理
    // ============================================================
    
    /// <summary>
    /// 启动工作流执行 (Protobuf 事件)
    /// </summary>
    [EventHandler]
    public async Task HandleStartWorkflowRequest(StartWorkflowRequestEvent request)
    {
        Logger.LogInformation("Coordinator {Id} starting workflow: {WorkflowName}",
            Id, request.WorkflowName);
        
        CustomState.ExecutionId = Guid.NewGuid().ToString("N")[..16];
        CustomState.WorkflowName = request.WorkflowName;
        CustomState.Status = ExecutionStatus.EsRunning;
        CustomState.CurrentPhase = "Starting";
        
        try
        {
            // 获取工作流定义
            var workflow = _workflowRegistry.Get(request.WorkflowName);
            if (workflow == null)
            {
                await FailExecutionAsync($"Workflow '{request.WorkflowName}' not found");
                return;
            }
            
            // 注入初始变量
            _workflowVariables.Clear();
            foreach (var (key, value) in request.Variables)
            {
                _workflowVariables[key] = ConvertFromProtoValue(value);
            }
            
            // 执行工作流
            await ExecuteWorkflowAsync(workflow);
            
            // 完成
            CustomState.Status = ExecutionStatus.EsCompleted;
            CustomState.CurrentPhase = "Completed";
            
            await PublishAsync(new WorkflowCompletedEventProto
            {
                ExecutionId = CustomState.ExecutionId,
                Success = true,
                Result = _workflowVariables.GetValueOrDefault("_output")?.ToString() ?? ""
            });
        }
        catch (Exception ex)
        {
            await FailExecutionAsync(ex.Message);
        }
    }
    
    /// <summary>
    /// 处理 Worker 完成事件 (Protobuf 事件)
    /// </summary>
    [EventHandler]
    public Task HandleStepCompletedEvent(StepCompletedEventProto evt)
    {
        Logger.LogDebug("Coordinator received step completed: {StepId} from {WorkerId}",
            evt.StepId, evt.WorkerId);
        
        // 更新统计
        CustomState.TotalTokensUsed += evt.TokensUsed;
        CustomState.TotalLlmCalls += evt.LlmCalls;
        
        // 收集结果
        _collectedResults[evt.RequestId] = evt;
        
        // 检查是否所有结果已收集
        if (_collectedResults.Count >= _expectedResults)
        {
            _fanOutCompletionSource?.TrySetResult(true);
        }
        
        return Task.CompletedTask;
    }
    
    // ============================================================
    //  工作流执行
    // ============================================================
    
    private async Task ExecuteWorkflowAsync(WorkflowDefinition workflow)
    {
        foreach (var step in workflow.Steps)
        {
            CustomState.CurrentPhase = $"Step: {step.Id}";
            CustomState.CurrentStepId = step.Id;
            
            var result = await ExecuteStepAsync(step);
            
            if (!result.Success)
            {
                throw new Exception($"Step '{step.Id}' failed: {result.Error}");
            }
            
            // 存储结果
            if (!string.IsNullOrEmpty(step.Store) && result.Value != null)
            {
                _workflowVariables[step.Store] = result.Value;
            }
        }
        
        // 构建输出
        _workflowVariables["_output"] = BuildOutput(workflow.Output);
    }
    
    private async Task<PrimitiveResult> ExecuteStepAsync(StepDefinition step)
    {
        return step.Type switch
        {
            // 简单步骤：Coordinator 直接执行
            "llm_call" => await ExecuteLlmCallDirectAsync(step),
            "conditional" => await ExecuteConditionalAsync(step),
            
            // 并行步骤：分发给 Workers (真正的 Actor 并行)
            "fan_out" => await ExecuteFanOutAsync(step),
            "parallel" => await ExecuteParallelAsync(step),
            
            // 其他
            "vote" => await ExecuteVoteAsync(step),
            "workflow_call" => await ExecuteWorkflowCallAsync(step),
            "checkpoint" => await ExecuteCheckpointAsync(step),
            
            _ => PrimitiveResult.Fail($"Unknown step type: {step.Type}")
        };
    }
    
    // ============================================================
    //  简单步骤 - Coordinator 直接执行
    // ============================================================
    
    private async Task<PrimitiveResult> ExecuteLlmCallDirectAsync(StepDefinition step)
    {
        var prompt = step.Parameters.GetValueOrDefault("prompt")?.ToString() ?? "";
        var systemPrompt = step.Parameters.GetValueOrDefault("system")?.ToString();
        var outputType = step.Parameters.GetValueOrDefault("output")?.ToString() ?? "text";
        
        // 渲染模板
        prompt = _templateEngine.Render(prompt, _workflowVariables);
        if (systemPrompt != null)
        {
            systemPrompt = _templateEngine.Render(systemPrompt, _workflowVariables);
        }
        
        // 调用 LLM
        var request = new AI.Abstractions.AevatarLLMRequest
        {
            SystemPrompt = systemPrompt,
            UserPrompt = prompt
        };
        
        var response = await LLMProvider.GenerateAsync(request);
        
        // 更新统计
        var promptTokens = response.Usage?.PromptTokens ?? 0;
        var completionTokens = response.Usage?.CompletionTokens ?? 0;
        CustomState.TotalTokensUsed += promptTokens + completionTokens;
        CustomState.TotalLlmCalls++;
        
        // 解析输出
        var parser = _parserFactory.Create(outputType);
        var parsed = parser.Parse(response.Content);
        
        return PrimitiveResult.Ok(parsed, promptTokens + completionTokens, 1);
    }
    
    // ============================================================
    //  并行步骤 - 分发给 Workers (真正的 Actor 并行)
    // ============================================================
    
    private async Task<PrimitiveResult> ExecuteFanOutAsync(StepDefinition step)
    {
        // 获取迭代列表
        var forEachVar = step.ForEach ?? "";
        if (!_workflowVariables.TryGetValue(forEachVar, out var itemsObj))
        {
            return PrimitiveResult.Fail($"Variable '{forEachVar}' not found");
        }
        
        var items = ConvertToList(itemsObj);
        if (items.Count == 0)
        {
            return PrimitiveResult.Ok(new List<object>());
        }
        
        var childStep = step.Step;
        if (childStep == null)
        {
            return PrimitiveResult.Fail("fan_out requires 'step' definition");
        }
        
        // 检查是否有 Workers
        if (_workerIds.Count == 0)
        {
            Logger.LogWarning("No workers available, falling back to sequential execution");
            return await ExecuteFanOutSequentialAsync(step, items, childStep);
        }
        
        Logger.LogInformation("Fan-out executing {Count} items across {Workers} workers",
            items.Count, _workerIds.Count);
        
        // 准备收集结果
        _collectedResults.Clear();
        _expectedResults = items.Count;
        _fanOutCompletionSource = new TaskCompletionSource<bool>();
        
        // 分发任务给 Workers（真正的 Actor 并行）
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            
            // 构建子上下文
            var childVariables = new Dictionary<string, object>(_workflowVariables)
            {
                ["item"] = item,
                ["index"] = i
            };
            
            // 创建 Protobuf 请求事件
            var request = new ExecuteStepRequestEvent
            {
                RequestId = $"{CustomState.ExecutionId}-{step.Id}-{i}",
                StepId = $"{step.Id}[{i}]",
                StepType = childStep.Type
            };
            
            // 转换参数和变量为 Protobuf
            foreach (var (key, value) in childStep.Parameters)
            {
                request.Parameters[key] = ConvertToProtoValue(value);
            }
            foreach (var (key, value) in childVariables)
            {
                request.Variables[key] = ConvertToProtoValue(value);
            }
            
            // 发送给 Workers（向下广播，所有 Children 都会收到）
            // Workers 根据 RequestId 判断是否处理
            await PublishAsync(request, EventDirection.Down);
        }
        
        // 等待所有 Worker 完成（事件驱动，非阻塞等待）
        var timeout = TimeSpan.FromMinutes(10);
        var completed = await Task.WhenAny(
            _fanOutCompletionSource.Task,
            Task.Delay(timeout));
        
        if (completed != _fanOutCompletionSource.Task)
        {
            return PrimitiveResult.Fail($"Fan-out timed out after {timeout}");
        }
        
        // 收集结果
        var results = _collectedResults.Values
            .OrderBy(r => r.StepId)
            .Select(r => r.Success ? (object)r.Result : null!)
            .Where(r => r != null)
            .ToList();
        
        // 汇聚
        var reducer = step.Reduce ?? "collect";
        var reduced = ApplyReducer(results, reducer);
        
        var totalTokens = _collectedResults.Values.Sum(r => r.TokensUsed);
        var totalCalls = _collectedResults.Values.Sum(r => r.LlmCalls);
        
        return PrimitiveResult.Ok(reduced, totalTokens, totalCalls);
    }
    
    /// <summary>
    /// 无 Worker 时的顺序执行回退
    /// </summary>
    private async Task<PrimitiveResult> ExecuteFanOutSequentialAsync(
        StepDefinition step, List<object> items, StepDefinition childStep)
    {
        var results = new List<object>();
        var totalTokens = 0;
        var totalCalls = 0;
        
        for (int i = 0; i < items.Count; i++)
        {
            // 临时设置变量
            _workflowVariables["item"] = items[i];
            _workflowVariables["index"] = i;
            
            var result = await ExecuteStepAsync(childStep);
            
            totalTokens += result.TokensUsed;
            totalCalls += result.LlmCalls;
            
            if (result.Success && result.Value != null)
            {
                results.Add(result.Value);
            }
        }
        
        // 清理临时变量
        _workflowVariables.Remove("item");
        _workflowVariables.Remove("index");
        
        var reducer = step.Reduce ?? "collect";
        var reduced = ApplyReducer(results, reducer);
        
        return PrimitiveResult.Ok(reduced, totalTokens, totalCalls);
    }
    
    private async Task<PrimitiveResult> ExecuteParallelAsync(StepDefinition step)
    {
        var steps = step.Parameters.GetValueOrDefault("steps") as List<StepDefinition>;
        if (steps == null || steps.Count == 0)
        {
            return PrimitiveResult.Ok(new Dictionary<string, object?>());
        }
        
        // 如果没有 Workers，顺序执行
        if (_workerIds.Count == 0)
        {
            var outputs = new Dictionary<string, object?>();
            var totalTokens = 0;
            var totalCalls = 0;
            
            foreach (var childStep in steps)
            {
                var result = await ExecuteStepAsync(childStep);
                totalTokens += result.TokensUsed;
                totalCalls += result.LlmCalls;
                
                if (!string.IsNullOrEmpty(childStep.Store) && result.Value != null)
                {
                    outputs[childStep.Store] = result.Value;
                    _workflowVariables[childStep.Store] = result.Value;
                }
            }
            
            return PrimitiveResult.Ok(outputs, totalTokens, totalCalls);
        }
        
        // 有 Workers 时并行执行
        _collectedResults.Clear();
        _expectedResults = steps.Count;
        _fanOutCompletionSource = new TaskCompletionSource<bool>();
        
        for (int i = 0; i < steps.Count; i++)
        {
            var childStep = steps[i];
            
            var request = new ExecuteStepRequestEvent
            {
                RequestId = $"{CustomState.ExecutionId}-parallel-{i}",
                StepId = childStep.Id,
                StepType = childStep.Type
            };
            
            foreach (var (key, value) in childStep.Parameters)
            {
                request.Parameters[key] = ConvertToProtoValue(value);
            }
            foreach (var (key, value) in _workflowVariables)
            {
                request.Variables[key] = ConvertToProtoValue(value);
            }
            
            await PublishAsync(request, EventDirection.Down);
        }
        
        // 等待完成
        await _fanOutCompletionSource.Task;
        
        // 汇总结果
        var outputs2 = new Dictionary<string, object?>();
        foreach (var result in _collectedResults.Values.Where(r => r.Success))
        {
            var originalStep = steps.FirstOrDefault(s => s.Id == result.StepId);
            if (originalStep?.Store != null)
            {
                outputs2[originalStep.Store] = result.Result;
                _workflowVariables[originalStep.Store] = result.Result;
            }
        }
        
        var totalTokens2 = _collectedResults.Values.Sum(r => r.TokensUsed);
        var totalCalls2 = _collectedResults.Values.Sum(r => r.LlmCalls);
        
        return PrimitiveResult.Ok(outputs2, totalTokens2, totalCalls2);
    }
    
    // ============================================================
    //  其他步骤
    // ============================================================
    
    private async Task<PrimitiveResult> ExecuteConditionalAsync(StepDefinition step)
    {
        var conditionExpr = step.Condition ?? "false";
        var conditionResult = _templateEngine.Evaluate(conditionExpr, _workflowVariables);
        
        var isTrue = conditionResult switch
        {
            bool b => b,
            string s => s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "yes",
            int i => i != 0,
            double d => d != 0,
            _ => conditionResult != null
        };
        
        var branch = isTrue ? step.IfTrue : step.IfFalse;
        if (branch == null || branch.Count == 0)
        {
            return PrimitiveResult.Ok(null);
        }
        
        // 执行分支
        PrimitiveResult? lastResult = null;
        foreach (var childStep in branch)
        {
            lastResult = await ExecuteStepAsync(childStep);
            if (!lastResult.Success) break;
            
            if (!string.IsNullOrEmpty(childStep.Store) && lastResult.Value != null)
            {
                _workflowVariables[childStep.Store] = lastResult.Value;
            }
        }
        
        return lastResult ?? PrimitiveResult.Ok(null);
    }
    
    private async Task<PrimitiveResult> ExecuteVoteAsync(StepDefinition step)
    {
        var k = ResolveIntParameter(step.Parameters, "k", 2);
        var maxRounds = ResolveIntParameter(step.Parameters, "max_rounds", 10);
        var similarity = ResolveFloatParameter(step.Parameters, "similarity", _semanticSimilarityThreshold);
        var generator = step.Generator;
        
        if (generator == null)
        {
            return PrimitiveResult.Fail("vote requires 'generator' definition");
        }
        
        // ─────────────────────────────────────────────
        //  使用 MAKER 的语义聚类 VoteEngine
        //  如果没有配置 embedding generator，自动回退到精确匹配
        // ─────────────────────────────────────────────
        using var engine = new VoteEngine(
            k,
            _embeddingGenerator,  // null 时自动回退到精确匹配
            maxRounds,
            similarity);
        
        var useSemanticClustering = _embeddingGenerator != null;
        Logger.LogDebug(
            "Vote step {StepId}: K={K}, maxRounds={MaxRounds}, semantic={Semantic}",
            step.Id, k, maxRounds, useSemanticClustering);
        
        var totalTokens = 0;
        var totalCalls = 0;
        VoteResult? consensusResult = null;
        var round = 0;
        
        while (consensusResult == null && round < maxRounds)
        {
            round++;
            
            var result = await ExecuteLlmCallDirectAsync(generator);
            totalTokens += result.TokensUsed;
            totalCalls += result.LlmCalls;
            
            if (result.Success)
            {
                var proposal = result.Value?.ToString() ?? "";
                consensusResult = await engine.SubmitVoteAsync(proposal);
                
                if (consensusResult != null)
                {
                    Logger.LogInformation(
                        "✓ Vote consensus reached at round {Round}: {LeaderVotes}/{K} votes",
                        round, consensusResult.LeaderVotes, k);
                }
            }
        }
        
        // 添加 embedding 调用计数
        var embeddingCalls = engine.EmbeddingCallCount;
        
        if (consensusResult != null && consensusResult.Success)
        {
            return PrimitiveResult.Ok(
                consensusResult.WinningContent, 
                totalTokens, 
                totalCalls + embeddingCalls);
        }
        
        // 没有达成共识，返回得票最多的
        var bestCandidate = engine.GetBestCandidate();
        var winnerContent = bestCandidate?.Content ?? "";
        
        Logger.LogWarning(
            "Vote step {StepId}: No consensus after {Rounds} rounds, using best candidate ({Votes} votes)",
            step.Id, maxRounds, bestCandidate?.Votes ?? 0);
        
        return PrimitiveResult.Ok(winnerContent, totalTokens, totalCalls + embeddingCalls);
    }
    
    // ============================================================
    //  参数解析辅助方法
    // ============================================================
    
    private int ResolveIntParameter(Dictionary<string, object?> parameters, string key, int defaultValue)
    {
        var value = ParameterExtensions.GetOptional<object>(parameters, key, defaultValue);
        
        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            string s when int.TryParse(s, out var parsed) => parsed,
            string s => (int)(_templateEngine.Evaluate(s, _workflowVariables) ?? defaultValue),
            _ => defaultValue
        };
    }
    
    private float ResolveFloatParameter(Dictionary<string, object?> parameters, string key, float defaultValue)
    {
        var value = ParameterExtensions.GetOptional<object>(parameters, key, defaultValue);
        
        return value switch
        {
            float f => f,
            double d => (float)d,
            int i => i,
            string s when float.TryParse(s, out var parsed) => parsed,
            string s => (float)(_templateEngine.Evaluate(s, _workflowVariables) ?? defaultValue),
            _ => defaultValue
        };
    }
    
    private async Task<PrimitiveResult> ExecuteWorkflowCallAsync(StepDefinition step)
    {
        var workflowName = step.Workflow ?? "";
        var workflow = _workflowRegistry.Get(workflowName);
        
        if (workflow == null)
        {
            return PrimitiveResult.Fail($"Workflow '{workflowName}' not found");
        }
        
        // 检查递归深度
        CustomState.CurrentDepth++;
        if (CustomState.CurrentDepth > CustomState.MaxDepth)
        {
            CustomState.CurrentDepth--;
            return PrimitiveResult.Fail($"Max recursion depth {CustomState.MaxDepth} exceeded");
        }
        
        try
        {
            // 构建子上下文
            var childVariables = new Dictionary<string, object>(_workflowVariables);
            if (step.Params != null)
            {
                foreach (var (key, value) in step.Params)
                {
                    if (value != null)
                    {
                        childVariables[key] = _templateEngine.ResolveValue(value, _workflowVariables)!;
                    }
                }
            }
            
            // 临时替换变量
            var savedVariables = new Dictionary<string, object>(_workflowVariables);
            _workflowVariables.Clear();
            foreach (var (key, value) in childVariables)
            {
                _workflowVariables[key] = value;
            }
            
            // 执行子工作流
            await ExecuteWorkflowAsync(workflow);
            
            var output = _workflowVariables.GetValueOrDefault("_output");
            
            // 恢复变量
            _workflowVariables.Clear();
            foreach (var (key, value) in savedVariables)
            {
                _workflowVariables[key] = value;
            }
            
            return PrimitiveResult.Ok(output);
        }
        finally
        {
            CustomState.CurrentDepth--;
        }
    }
    
    private Task<PrimitiveResult> ExecuteCheckpointAsync(StepDefinition step)
    {
        Logger.LogDebug("Checkpoint at step: {StepId}", step.Id);
        return Task.FromResult(PrimitiveResult.Ok(null));
    }
    
    // ============================================================
    //  辅助方法
    // ============================================================
    
    private async Task FailExecutionAsync(string error)
    {
        CustomState.Status = ExecutionStatus.EsFailed;
        CustomState.CurrentPhase = "Failed";
        CustomState.Error = error;
        
        await PublishAsync(new WorkflowCompletedEventProto
        {
            ExecutionId = CustomState.ExecutionId,
            Success = false,
            Error = error
        });
    }
    
    private object BuildOutput(Dictionary<string, string> outputDef)
    {
        if (outputDef.Count == 0)
            return _workflowVariables;
        
        var output = new Dictionary<string, object?>();
        foreach (var (key, template) in outputDef)
        {
            output[key] = _templateEngine.ResolveValue(template, _workflowVariables);
        }
        return output;
    }
    
    private static List<object> ConvertToList(object items)
    {
        return items switch
        {
            IEnumerable<object> enumerable => enumerable.ToList(),
            System.Collections.IList list => list.Cast<object>().ToList(),
            System.Collections.IEnumerable enumerable => enumerable.Cast<object>().ToList(),
            _ => [items]
        };
    }
    
    private static object ApplyReducer(List<object> results, string reducer)
    {
        return reducer.ToLowerInvariant() switch
        {
            "collect" => results,
            "flatten" => results.SelectMany(r => r switch
            {
                IEnumerable<object> list => list,
                System.Collections.IList list => list.Cast<object>(),
                _ => new[] { r }
            }).ToList(),
            "first" => results.FirstOrDefault()!,
            "last" => results.LastOrDefault()!,
            "concat" => string.Join("\n", results.Select(r => r.ToString())),
            _ => results
        };
    }
    
    // ============================================================
    //  Protobuf 转换
    // ============================================================
    
    private static object ConvertFromProtoValue(Value value)
    {
        return value.KindCase switch
        {
            Value.KindOneofCase.NullValue => null!,
            Value.KindOneofCase.NumberValue => value.NumberValue,
            Value.KindOneofCase.StringValue => value.StringValue,
            Value.KindOneofCase.BoolValue => value.BoolValue,
            Value.KindOneofCase.StructValue => ConvertFromProtoStruct(value.StructValue),
            Value.KindOneofCase.ListValue => value.ListValue.Values
                .Select(ConvertFromProtoValue)
                .ToList(),
            _ => value.ToString()
        };
    }
    
    private static Dictionary<string, object> ConvertFromProtoStruct(Struct protoStruct)
    {
        var result = new Dictionary<string, object>();
        foreach (var (key, value) in protoStruct.Fields)
        {
            result[key] = ConvertFromProtoValue(value);
        }
        return result;
    }
    
    private static Value ConvertToProtoValue(object? value)
    {
        return value switch
        {
            null => Value.ForNull(),
            bool b => Value.ForBool(b),
            int i => Value.ForNumber(i),
            long l => Value.ForNumber(l),
            float f => Value.ForNumber(f),
            double d => Value.ForNumber(d),
            string s => Value.ForString(s),
            IEnumerable<object> list => Value.ForList(list.Select(ConvertToProtoValue).ToArray()),
            IDictionary<string, object> dict => ConvertDictToProtoValue(dict),
            _ => Value.ForString(value.ToString() ?? "")
        };
    }
    
    private static Value ConvertDictToProtoValue(IDictionary<string, object> dict)
    {
        var structValue = new Struct();
        foreach (var (key, value) in dict)
        {
            structValue.Fields[key] = ConvertToProtoValue(value);
        }
        return Value.ForStruct(structValue);
    }
}

