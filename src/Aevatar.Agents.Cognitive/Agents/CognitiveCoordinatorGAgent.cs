using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Cognitive.Engine;
using Aevatar.Agents.Cognitive.Messages;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Template;
using Aevatar.Agents.Cognitive.Utilities;
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
    private readonly ConcurrentDictionary<string, string> _fanOutChildTypes = new();
    private readonly ConcurrentDictionary<string, string> _fanOutUserPrompts = new();
    private readonly ConcurrentDictionary<string, string> _fanOutSystemPrompts = new();
    private int _expectedResults;
    private int _fanOutSuccessCount;
    private TaskCompletionSource<bool>? _fanOutCompletionSource;
    private StepDefinition? _currentFanOutStep;

    // Worker 管理（由外部注入）
    private IGAgentActorManager? _actorManager;
    private readonly List<Guid> _workerIds = [];

    // 语义聚类投票 (可选)
    private IEmbeddingGenerator<string, Embedding<float>>? _embeddingGenerator;
    private float _semanticSimilarityThreshold = 0.85f;

    // Red-Flagging (可选, 可插拔)
    private IRedFlagStrategy? _redFlagStrategy;
    private IRedFlagHandler _redFlagHandler = new DefaultRedFlagHandler();

    // 步骤事件追踪（用于前端可视化）
    private readonly List<WorkflowStepEvent> _stepEvents = [];
    private readonly ConcurrentDictionary<string, DateTime> _stepStartTimes = new();
    private Action<WorkflowStepEvent>? _onStepEvent;
    private readonly object _stepEventsLock = new(); // vote 并行生成时可能并发写入
    private readonly object _statsLock = new(); // 多并行任务下累加统计，避免丢失/错乱

    // ============================================================
    //  构造函数
    // ============================================================

    public CognitiveCoordinatorGAgent()
    {
    }

    public CognitiveCoordinatorGAgent(Guid id) : base(id)
    {
    }

    // ============================================================
    //  生命周期
    // ============================================================

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        // ============================================================
        //  递归深度上限（默认值）
        //
        //  NOTE:
        //  - 最终上限应由工作流输入变量 `max_depth` 决定（见 HandleStartWorkflowRequest）。
        //  - 这里仅提供一个“启动默认值”，避免未配置时无限递归。
        // ============================================================
        CustomState.MaxDepth = 50;
        CustomState.Status = ExecutionStatus.EsPending;

        // 默认开启 Red-Flag 策略，限制最大内容长度 102400
        _redFlagStrategy ??= new DefaultEnglishRedFlagStrategy(new RedFlagOptions
        {
            MaxContentLength = 102400,
            EnableLengthValidation = true
        });

        // DEBUG: 验证 Logger 是否注入
        var loggerType = Logger?.GetType().Name ?? "null";
        Console.WriteLine($"[DEBUG] CognitiveCoordinatorGAgent activated. Logger type: {loggerType}, Id: {Id}");
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
    /// 设置步骤事件回调（用于实时可视化）
    /// </summary>
    public void SetStepEventCallback(Action<WorkflowStepEvent> callback)
    {
        _onStepEvent = callback;
    }

    /// <summary>
    /// 获取所有步骤事件（用于回放）
    /// </summary>
    public IReadOnlyList<WorkflowStepEvent> GetStepEvents()
    {
        // NOTE:
        // - vote / fan_out 可能并发写 _stepEvents
        // - 直接暴露 List 会导致读取端枚举时抛异常或读到撕裂数据
        lock (_stepEventsLock)
        {
            return _stepEvents.ToList();
        }
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
    }

    /// <summary>
    /// 设置 Red-Flag 策略（用于过滤无效 LLM 响应）
    /// 可插拔设计：
    /// - null: 禁用 Red-Flag 检测（默认）
    /// - DefaultEnglishRedFlagStrategy: 英文内容验证
    /// - ChineseRedFlagStrategy: 中文内容验证
    /// - CodeAwareRedFlagStrategy: 代码内容验证
    /// - 自定义实现 IRedFlagStrategy
    /// </summary>
    public void SetRedFlagStrategy(IRedFlagStrategy? strategy, IRedFlagHandler? handler = null)
    {
        _redFlagStrategy = strategy;
        _redFlagHandler = handler ?? new DefaultRedFlagHandler();

        Logger.LogInformation(
            "Red-Flag strategy configured: {Strategy}",
            strategy?.GetType().Name ?? "disabled");
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

        // Worker needs the same LLM provider as coordinator.
        // Coordinator is expected to be initialized by CognitiveStrategy before creating the pool.
        var providerName = ActiveProviderConfig?.Name;
        if (string.IsNullOrWhiteSpace(providerName))
        {
            Logger.LogWarning("Coordinator is not initialized with an LLM provider yet. Workers will NOT be initialized and fan_out will fail.");
        }

        for (int i = 0; i < poolSize; i++)
        {
            // 创建 Worker Actor
            var workerId = Guid.NewGuid();
            var workerActor = await _actorManager.CreateAndRegisterAsync<CognitiveWorkerGAgent>(workerId);

            // 初始化 Worker 的 LLM Provider（否则 Worker.LLMProvider 会抛异常）
            if (!string.IsNullOrWhiteSpace(providerName))
            {
                if (workerActor.GetAgent() is CognitiveWorkerGAgent worker)
                {
                    await worker.InitializeAsync(providerName!, cancellationToken: CancellationToken.None);
                }
            }

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
        Error = string.IsNullOrWhiteSpace(CustomState.Error) ? null : CustomState.Error,
        TotalTokens = CustomState.TotalTokensUsed,
        TotalLlmCalls = CustomState.TotalLlmCalls
    };

    // ============================================================
    //  事件处理
    // ============================================================

    /// <summary>
    /// 直接启动工作流执行（API 调用）
    /// </summary>
    public Task StartWorkflowAsync(string workflowName, Dictionary<string, object>? variables = null)
    {
        var request = new StartWorkflowRequestEvent { WorkflowName = workflowName };
        if (variables != null)
        {
            foreach (var (key, value) in variables)
            {
                request.Variables[key] = ProtoValueConverter.ToProto(value);
            }
        }

        return HandleStartWorkflowRequest(request);
    }

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

            // 1. 先应用 inputs 的默认值
            foreach (var input in workflow.Inputs)
            {
                if (input.DefaultValue != null)
                {
                    _workflowVariables[input.Name] = input.DefaultValue;
                }
            }

            // 2. 再覆盖为传入的变量（传入的优先级更高）
            foreach (var (key, value) in request.Variables)
            {
                _workflowVariables[key] = ProtoValueConverter.FromProto(value);
            }

            // ============================================================
            //  输入驱动的 MaxDepth（消灭硬编码）
            //
            //  约定：
            //  - 统一使用 `max_depth` 作为工作流递归深度控制输入
            //  - 若未提供，则保留 OnActivateAsync 的默认值或 YAML input 默认值
            // ============================================================
            if (TryGetPositiveInt(_workflowVariables, "max_depth", out var inputMaxDepth))
            {
                // 安全阀：避免配置错误导致极端深度把系统打爆
                CustomState.MaxDepth = Math.Clamp(inputMaxDepth, 1, 200);
            }

            // DEBUG: 输出变量内容
            var taskLen = _workflowVariables.GetValueOrDefault("task")?.ToString()?.Length ?? 0;
            Console.WriteLine(
                $"[DEBUG][Workflow Start] Variables: [{string.Join(", ", _workflowVariables.Keys)}], task length={taskLen}");

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
            Logger.LogError(ex, "[WORKFLOW] Execution failed: {Message}", ex.Message);
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
        // fan_out completion semantics:
        // - streaming 中间态：Success=false && Error=""（不计入完成）
        // - 终态完成：Success=true 或 Success=false 且 Error!= ""（失败也算“完成”，否则 fan_out 会无意义卡住）
        _fanOutChildTypes.TryGetValue(evt.RequestId, out var childType);
        var isStreaming = !evt.Success && string.IsNullOrEmpty(evt.Error);
        var isTerminal = evt.Success || (!evt.Success && !string.IsNullOrEmpty(evt.Error));
        if (evt.Success)
        {
            _fanOutSuccessCount++;
        }
        if (isTerminal)
        {
            _fanOutChildTypes.TryRemove(evt.RequestId, out _);
        }

        // 子任务完成事件（用于前端并行可视化）
        if (!string.IsNullOrEmpty(evt.StepId) && !string.IsNullOrEmpty(childType))
        {
            var childDef = new StepDefinition
            {
                Id = evt.StepId,
                Type = childType
            };

            // UI semantics:
            // - streaming: Running
            // - terminal fail: Failed
            // - terminal success: Completed
            var status = evt.Success ? StepStatus.Completed : (isStreaming ? StepStatus.Running : StepStatus.Failed);
            var userPrompt = _fanOutUserPrompts.TryGetValue(evt.RequestId, out var up) ? up : "";
            var systemPrompt = _fanOutSystemPrompts.TryGetValue(evt.RequestId, out var sp) ? sp : "";
            EmitStepEvent(childDef,
                status,
                evt.Success
                    ? "Subtask completed"
                    : (string.IsNullOrEmpty(evt.Error) ? "Subtask streaming" : $"Subtask failed: {evt.Error}"),
                progress: evt.Success ? 1.0f : 0.0f,
                parentStepId: _currentFanOutStep?.Id,
                assistantResponse: evt.Result,
                systemPrompt: systemPrompt,
                userPrompt: userPrompt);
        }

        // 发送并行进度事件
        if (_currentFanOutStep != null)
        {
            var succeeded = _fanOutSuccessCount;
            var failed = _collectedResults.Values.Count(r => !r.Success && !string.IsNullOrEmpty(r.Error));
            var terminal = _collectedResults.Values.Count(r => r.Success || !string.IsNullOrEmpty(r.Error));

            EmitStepEvent(_currentFanOutStep, StepStatus.Running,
                $"Progress: {terminal}/{_expectedResults} (ok: {succeeded}, failed: {failed})",
                progress: (float)terminal / _expectedResults,
                parallelTotal: _expectedResults,
                // parallelCompleted 表示“终态完成数”（成功+失败），避免失败导致永远不满格
                parallelCompleted: terminal,
                parallelFailed: failed);
        }

        // 检查是否所有结果已收集
        // 终态完成（成功或失败）都算“收集完毕”
        if (_collectedResults.Values.Count(r => r.Success || !string.IsNullOrEmpty(r.Error)) >= _expectedResults)
        {
            _currentFanOutStep = null; // 清除当前 fan-out 步骤
            _fanOutCompletionSource?.TrySetResult(true);
        }

        return Task.CompletedTask;
    }

    // ============================================================
    //  工作流执行
    // ============================================================

    private async Task ExecuteWorkflowAsync(WorkflowDefinition workflow)
    {
        Logger.LogInformation("[DEBUG][Workflow] Executing workflow '{Name}' with {Count} steps: [{Steps}]",
            workflow.Name, workflow.Steps.Count, string.Join(", ", workflow.Steps.Select(s => s.Id)));

        for (int stepIndex = 0; stepIndex < workflow.Steps.Count; stepIndex++)
        {
            var step = workflow.Steps[stepIndex];
            Logger.LogInformation("[DEBUG][Workflow] >>> Executing step {Index}/{Total}: '{StepId}' (type={Type})",
                stepIndex + 1, workflow.Steps.Count, step.Id, step.Type);

            CustomState.CurrentPhase = $"Step: {step.Id}";
            CustomState.CurrentStepId = step.Id;

            var result = await ExecuteStepAsync(step);

            if (!result.Success)
            {
                throw new Exception($"Step '{step.Id}' failed: {result.Error}");
            }

            // 存储结果
            if (!string.IsNullOrEmpty(step.Store))
            {
                Logger.LogInformation(
                    "[DEBUG][Workflow] Step '{StepId}' store='{Store}', result.Value is null: {IsNull}",
                    step.Id, step.Store, result.Value == null);

                if (result.Value != null)
                {
                    _workflowVariables[step.Store] = result.Value;
                    Logger.LogInformation("[DEBUG][Workflow] Stored '{Store}' type: {Type}",
                        step.Store, result.Value.GetType().FullName);
                }
                else
                {
                    Logger.LogWarning("[DEBUG][Workflow] ⚠️ Step '{StepId}' returned null, NOT storing to '{Store}'",
                        step.Id, step.Store);
                }
            }
        }

        // 构建输出
        _workflowVariables["_output"] = BuildOutput(workflow.Output);
    }

    private async Task<PrimitiveResult> ExecuteStepAsync(StepDefinition step)
    {
        // 预渲染 prompt 用于开始事件（确保 WORKERS 面板能显示完整提示词）
        string? preRenderedPrompt = null;
        string? preRenderedSystem = null;

        if (step.Type == "llm_call")
        {
            var rawPrompt = step.Parameters.GetValueOrDefault("prompt")?.ToString() ?? "";
            var rawSystem = step.Parameters.GetValueOrDefault("system")?.ToString();

            preRenderedPrompt = _templateEngine.Render(rawPrompt, _workflowVariables);
            if (rawSystem != null)
                preRenderedSystem = _templateEngine.Render(rawSystem, _workflowVariables);

            // 调试：如果 task 变量为空，记录警告
            if (rawPrompt.Contains("{{task}}") &&
                string.IsNullOrWhiteSpace(_workflowVariables.GetValueOrDefault("task")?.ToString()))
            {
                Logger.LogWarning("[{Step}] WARNING: task variable is empty! Available vars: {Vars}",
                    step.Id, string.Join(", ", _workflowVariables.Keys));
            }
        }

        // 发送开始事件（包含预渲染的 prompt）
        EmitStepEvent(step, StepStatus.Running,
            userPrompt: preRenderedPrompt,
            systemPrompt: preRenderedSystem);

        try
        {
            var result = step.Type switch
            {
                // 简单步骤：Coordinator 直接执行（传递预渲染的 prompt 避免重复渲染）
                "llm_call" => await ExecuteLlmCallDirectAsync(step, preRenderedPrompt, preRenderedSystem),
                "conditional" => await ExecuteConditionalAsync(step),

                // 并行步骤：分发给 Workers (真正的 Actor 并行)
                "fan_out" => await ExecuteFanOutAsync(step),
                "parallel" => await ExecuteParallelAsync(step),

                // 其他
                "vote" => await ExecuteVoteAsync(step),
                "workflow_call" => await ExecuteWorkflowCallAsync(step),
                "checkpoint" => await ExecuteCheckpointAsync(step),
                "assign" => await ExecuteAssignAsync(step),

                _ => PrimitiveResult.Fail($"Unknown step type: {step.Type}")
            };

            // 发送完成/失败事件（包含对话记录）
            EmitStepEvent(step,
                result.Success ? StepStatus.Completed : StepStatus.Failed,
                result.Success ? null : result.Error,
                progress: 1.0f,
                systemPrompt: result.SystemPrompt,
                userPrompt: result.UserPrompt,
                assistantResponse: result.AssistantResponse);

            return result;
        }
        catch (Exception ex)
        {
            EmitStepEvent(step, StepStatus.Failed, ex.Message);
            throw;
        }
    }

    // ============================================================
    //  简单步骤 - Coordinator 直接执行
    // ============================================================

    private async Task<PrimitiveResult> ExecuteLlmCallDirectAsync(
        StepDefinition step,
        string? preRenderedPrompt = null,
        string? preRenderedSystem = null)
    {
        var outputType = step.Parameters.GetValueOrDefault("output")?.ToString() ?? "text";
        const int MaxContentLength = 102400;

        // 使用预渲染的 prompt（如果提供），否则现场渲染
        var prompt = preRenderedPrompt ?? _templateEngine.Render(
            step.Parameters.GetValueOrDefault("prompt")?.ToString() ?? "",
            _workflowVariables);

        var systemPrompt = preRenderedSystem;
        if (systemPrompt == null && step.Parameters.TryGetValue("system", out var sysObj) && sysObj != null)
        {
            systemPrompt = _templateEngine.Render(sysObj.ToString()!, _workflowVariables);
        }

        // 构建请求
        var request = new AI.Abstractions.AevatarLLMRequest
        {
            SystemPrompt = systemPrompt,
            UserPrompt = prompt
        };

        // 尝试流式
        var modelInfo = await LLMProvider.GetModelInfoAsync();
        var supportsStreaming = modelInfo.SupportsStreaming;

        var output = string.Empty;
        var promptTokens = 0;
        var completionTokens = 0;

        if (supportsStreaming)
        {
            var sb = new System.Text.StringBuilder();
            var tokenIndex = 0;

            await foreach (var token in LLMProvider.GenerateStreamAsync(request))
            {
                var content = token.Content ?? string.Empty;
                if (string.IsNullOrEmpty(content) && !token.IsComplete)
                    continue;

                sb.Append(content);
                output = sb.ToString();

                // 中间流式事件（Running，带累计回答）
                EmitStepEvent(step, StepStatus.Running,
                    $"Streaming token {tokenIndex}",
                    progress: 0,
                    systemPrompt: systemPrompt,
                    userPrompt: prompt,
                    assistantResponse: output);

                tokenIndex++;

                if (token.IsComplete)
                {
                    // 流式模式下没有 Usage，先置零
                    promptTokens = 0;
                    completionTokens = tokenIndex;
                    break;
                }
            }
        }
        else
        {
            var response = await LLMProvider.GenerateAsync(request);
            output = response.Content ?? string.Empty;
            promptTokens = response.Usage?.PromptTokens ?? 0;
            completionTokens = response.Usage?.CompletionTokens ?? 0;
        }

        // 更新统计
        CustomState.TotalTokensUsed += promptTokens + completionTokens;
        CustomState.TotalLlmCalls++;

        // 解析输出
        if (output.Length > MaxContentLength)
        {
            return PrimitiveResult.Fail($"redflag-length>{MaxContentLength}");
        }

        var parser = _parserFactory.Create(outputType);
        var parsed = parser.Parse(output);
        if (parsed == null)
        {
            return PrimitiveResult.Fail("redflag-parse-null");
        }

        return new PrimitiveResult
        {
            Success = true,
            Value = parsed,
            TokensUsed = promptTokens + completionTokens,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            LlmCalls = 1,
            SystemPrompt = systemPrompt,
            UserPrompt = prompt,
            AssistantResponse = output
        };
    }

    /// <summary>
    /// 执行 LLM 调用，streaming 事件发送到指定的步骤
    /// 用于 vote 并行生成时，每个提案有独立的事件流
    /// </summary>
    private async Task<PrimitiveResult> ExecuteLlmCallWithStreamingAsync(
        StepDefinition generator,
        StepDefinition eventStep,
        string? systemPrompt,
        string userPrompt)
    {
        // ============================================================
        //  可靠性护栏：
        //  - vote 会并行发起多个 LLM 调用
        //  - 任一调用卡住不返回，会让 vote 卡死在 Task.WhenAll
        //  - 这里统一做：超时 + 异常收敛（超时/异常→返回失败 PrimitiveResult）
        // ============================================================

        Logger.LogInformation("[LLM] ▶ ExecuteLlmCallWithStreamingAsync ENTER for {StepId} (promptLen={Len})",
            eventStep.Id, userPrompt.Length);

        var outputType = generator.Parameters.GetValueOrDefault("output")?.ToString() ?? "text";
        const int MaxContentLength = 102400;

        // NOTE: 不绑定外部 CancellationToken（当前 Coordinator 执行链路未贯通），至少保证不会无限挂死
        var callTimeout = TimeSpan.FromMinutes(6);
        using var timeoutCts = new CancellationTokenSource(callTimeout);
        var ct = timeoutCts.Token;

        var request = new AI.Abstractions.AevatarLLMRequest
        {
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt
        };

        var output = string.Empty;
        var promptTokens = 0;
        var completionTokens = 0;

        try
        {
            Logger.LogInformation("[LLM] {StepId}: Getting model info...", eventStep.Id);
            var modelInfo = await LLMProvider.GetModelInfoAsync(ct);
            var supportsStreaming = modelInfo.SupportsStreaming;
            Logger.LogInformation("[LLM] {StepId}: supportsStreaming={Streaming}", eventStep.Id, supportsStreaming);

            if (supportsStreaming)
            {
                var sb = new System.Text.StringBuilder();
                var tokenIndex = 0;

                Logger.LogInformation("[STREAM] Starting streaming for {StepId}, Type={Type}",
                    eventStep.Id, eventStep.Type);

                await foreach (var token in LLMProvider.GenerateStreamAsync(request, ct).WithCancellation(ct))
                {
                    var content = token.Content ?? string.Empty;
                    if (string.IsNullOrEmpty(content) && !token.IsComplete)
                        continue;

                    sb.Append(content);
                    output = sb.ToString();

                    // 防止输出爆炸导致内存/渲染/日志被打穿（这类“卡住”看起来像死循环）
                    if (output.Length > MaxContentLength)
                    {
                        return PrimitiveResult.Fail($"redflag-length>{MaxContentLength}");
                    }

                    // 流式事件发送到指定的步骤（每个提案独立显示）
                    EmitStepEvent(eventStep, StepStatus.Running,
                        $"Streaming... ({tokenIndex} tokens)",
                        progress: 0,
                        systemPrompt: systemPrompt,
                        userPrompt: userPrompt,
                        assistantResponse: output);

                    tokenIndex++;

                    if (tokenIndex % 10 == 0)
                    {
                        Logger.LogDebug("[STREAM] {StepId}: {Tokens} tokens, len={Len}",
                            eventStep.Id, tokenIndex, output.Length);
                    }

                    if (token.IsComplete)
                    {
                        completionTokens = tokenIndex;
                        Logger.LogInformation("[STREAM] Completed {StepId}: {Tokens} tokens",
                            eventStep.Id, tokenIndex);
                        break;
                    }
                }

                // 某些 provider 不会显式发 IsComplete=true（枚举自然结束）
                if (completionTokens == 0 && !string.IsNullOrEmpty(output))
                {
                    completionTokens = Math.Max(1, output.Length / 4);
                }
            }
            else
            {
                var response = await LLMProvider.GenerateAsync(request, ct);
                output = response.Content ?? string.Empty;
                promptTokens = response.Usage?.PromptTokens ?? 0;
                completionTokens = response.Usage?.CompletionTokens ?? 0;
            }

            lock (_statsLock)
            {
                CustomState.TotalTokensUsed += promptTokens + completionTokens;
                CustomState.TotalLlmCalls++;
            }

            if (output.Length > MaxContentLength)
            {
                return PrimitiveResult.Fail($"redflag-length>{MaxContentLength}");
            }

            var parser = _parserFactory.Create(outputType);
            var parsed = parser.Parse(output);
            if (parsed == null)
            {
                return PrimitiveResult.Fail("redflag-parse-null");
            }

            return new PrimitiveResult
            {
                Success = true,
                Value = parsed,
                TokensUsed = promptTokens + completionTokens,
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                LlmCalls = 1,
                SystemPrompt = systemPrompt,
                UserPrompt = userPrompt,
                AssistantResponse = output
            };
        }
        catch (OperationCanceledException)
        {
            Logger.LogWarning("[LLM] ✗ Timeout/cancel: {StepId} after {Timeout} (promptLen={Len}, outLen={OutLen})",
                eventStep.Id, callTimeout, userPrompt.Length, output.Length);

            return new PrimitiveResult
            {
                Success = false,
                Error = $"llm-timeout>{(int)callTimeout.TotalSeconds}s",
                SystemPrompt = systemPrompt,
                UserPrompt = userPrompt,
                AssistantResponse = output,
                TokensUsed = 0,
                LlmCalls = 1
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LLM] ✗ Error in {StepId}: {Message}", eventStep.Id, ex.Message);

            return new PrimitiveResult
            {
                Success = false,
                Error = $"llm-error:{ex.Message}",
                SystemPrompt = systemPrompt,
                UserPrompt = userPrompt,
                AssistantResponse = output,
                TokensUsed = 0,
                LlmCalls = 1
            };
        }
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

        // workflow_call 类型需要 Coordinator 自己处理（递归），不能分发给 Worker
        // Worker 只支持 llm_call
        var isWorkflowCall = childStep.Type == "workflow_call";

        // 检查是否有 Workers（只有 llm_call 才分发）
        if (_workerIds.Count == 0 || isWorkflowCall)
        {
            if (isWorkflowCall)
            {
                Logger.LogInformation("Fan-out contains workflow_call, executing in Coordinator (parallel Tasks)");
            }
            else
            {
                Logger.LogWarning("No workers available, falling back to sequential execution");
            }

            return await ExecuteFanOutSequentialAsync(step, items, childStep);
        }

        Logger.LogInformation("Fan-out executing {Count} items across {Workers} workers",
            items.Count, _workerIds.Count);

        // 准备收集结果
        _collectedResults.Clear();
        _fanOutSuccessCount = 0;
        _expectedResults = items.Count;
        _fanOutCompletionSource = new TaskCompletionSource<bool>();
        _currentFanOutStep = step;
        _fanOutChildTypes.Clear();
        _fanOutUserPrompts.Clear();
        _fanOutSystemPrompts.Clear();

        // 发送 fan-out 开始事件
        EmitStepEvent(step, StepStatus.Running,
            $"Distributing {items.Count} tasks to {_workerIds.Count} workers",
            progress: 0,
            parallelTotal: items.Count, parallelCompleted: 0);

        // 分发任务给 Workers（真正的 Actor 并行）
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var targetWorker = _workerIds[i % _workerIds.Count];

            // 构建子上下文
            var childVariables = new Dictionary<string, object>(_workflowVariables)
            {
                ["item"] = item,
                ["index"] = i,
                // Ensure exactly-one worker executes this task (fan_out uses Down broadcast).
                // Worker will ignore if not matching its own Id.
                ["__target_worker"] = targetWorker.ToString("N")
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
                request.Parameters[key] = ProtoValueConverter.ToProto(value);
            }

            foreach (var (key, value) in childVariables)
            {
                request.Variables[key] = ProtoValueConverter.ToProto(value);
            }

            _fanOutChildTypes[request.RequestId] = childStep.Type;

            // 预渲染子任务的 prompt，后续用于前端展示
            var renderedPrompt = childStep.Parameters.GetValueOrDefault("prompt")?.ToString() ?? "";
            var renderedSystem = childStep.Parameters.GetValueOrDefault("system")?.ToString();
            renderedPrompt = _templateEngine.Render(renderedPrompt, childVariables);
            if (renderedSystem != null)
            {
                renderedSystem = _templateEngine.Render(renderedSystem, childVariables);
            }

            _fanOutUserPrompts[request.RequestId] = renderedPrompt;
            if (renderedSystem != null)
            {
                _fanOutSystemPrompts[request.RequestId] = renderedSystem;
            }

            // 为子任务发送开始事件（用于前端并行可视化）
            var childDef = new StepDefinition
            {
                Id = request.StepId,
                Type = childStep.Type
            };
            EmitStepEvent(childDef, StepStatus.Running,
                $"Executing subtask {i + 1}/{items.Count}",
                parentStepId: step.Id,
                systemPrompt: renderedSystem,
                userPrompt: renderedPrompt);

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
    /// Coordinator 内部串行执行（用于 workflow_call 或无 Worker 场景）
    /// 
    /// ⚠️ Orleans 兼容：
    /// - Grain 是 turn-based 单线程，不能用 Task.Run
    /// - 状态修改必须在 Grain 线程内
    /// - 真正的并行需要分发给 Worker Grains
    /// </summary>
    private async Task<PrimitiveResult> ExecuteFanOutSequentialAsync(
        StepDefinition step, List<object> items, StepDefinition childStep)
    {
        // 发送 fan-out 开始事件
        EmitStepEvent(step, StepStatus.Running,
            $"Executing {items.Count} subtasks (sequential in Coordinator)",
            progress: 0,
            parallelTotal: items.Count, parallelCompleted: 0);

        var results = new List<object>();
        var totalTokens = 0;
        var totalCalls = 0;

        // 串行执行每个子任务（Orleans 兼容）
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];

            // 为子任务发送开始事件
            var childDef = new StepDefinition { Id = $"{step.Id}[{i}]", Type = childStep.Type };

            // 预渲染 prompt（使用临时变量）
            var tempVars = new Dictionary<string, object>(_workflowVariables)
            {
                ["item"] = item,
                ["index"] = i
            };
            var userPrompt = childStep.Parameters.GetValueOrDefault("prompt")?.ToString();
            var systemPrompt = childStep.Parameters.GetValueOrDefault("system")?.ToString();
            if (userPrompt != null) userPrompt = _templateEngine.Render(userPrompt, tempVars);
            if (systemPrompt != null) systemPrompt = _templateEngine.Render(systemPrompt, tempVars);

            EmitStepEvent(childDef, StepStatus.Running,
                $"Executing subtask {i + 1}/{items.Count}",
                parentStepId: step.Id,
                systemPrompt: systemPrompt,
                userPrompt: userPrompt);

            // 临时设置变量
            _workflowVariables["item"] = item;
            _workflowVariables["index"] = i;

            try
            {
                var result = await ExecuteStepAsync(childStep);

                totalTokens += result.TokensUsed;
                totalCalls += result.LlmCalls;

                // 发送子任务完成事件
                EmitStepEvent(childDef, result.Success ? StepStatus.Completed : StepStatus.Failed,
                    result.Success ? $"Subtask {i + 1} completed" : $"Subtask {i + 1} failed: {result.Error}",
                    progress: 1,
                    parentStepId: step.Id,
                    assistantResponse: result.AssistantResponse);

                // DEBUG: 检查子任务结果
                Logger.LogInformation(
                    "[DEBUG][FanOut] Subtask {Index} result: Success={Success}, Value is null={IsNull}, Value type={Type}",
                    i, result.Success, result.Value == null, result.Value?.GetType().FullName ?? "null");

                if (result.Success && result.Value != null)
                {
                    results.Add(result.Value);
                    Logger.LogInformation("[DEBUG][FanOut] Added subtask {Index} result to collection, total: {Count}",
                        i, results.Count);
                }
                else if (result.Success && result.Value == null)
                {
                    Logger.LogWarning(
                        "[DEBUG][FanOut] ⚠️ Subtask {Index} succeeded but Value is null, NOT added to results!", i);
                }

                // 发送进度事件
                EmitStepEvent(step, StepStatus.Running,
                    $"Progress: {i + 1}/{items.Count} subtasks completed",
                    progress: (float)(i + 1) / items.Count,
                    parallelTotal: items.Count, parallelCompleted: i + 1);
            }
            finally
            {
                // 清理临时变量
                _workflowVariables.Remove("item");
                _workflowVariables.Remove("index");
            }
        }

        // 发送 fan-out 完成事件
        EmitStepEvent(step, StepStatus.Completed,
            $"All {items.Count} subtasks completed",
            progress: 1,
            parallelTotal: items.Count, parallelCompleted: results.Count);

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
                request.Parameters[key] = ProtoValueConverter.ToProto(value);
            }

            foreach (var (key, value) in _workflowVariables)
            {
                request.Variables[key] = ProtoValueConverter.ToProto(value);
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
        object? conditionResult;
        string? evalError = null;
        try
        {
            conditionResult = _templateEngine.Evaluate(conditionExpr, _workflowVariables);
        }
        catch (Exception ex)
        {
            // IMPORTANT:
            // - conditional 是控制流关键节点（stop_or_continue / ensure_state）
            // - 这里抛异常会直接 FailExecutionAsync，表现为“系统彻底停了”
            // - 策略：默认走 if_false（继续执行），并把错误记录下来（可从 UI/日志看到）
            conditionResult = false;
            evalError = $"conditional-eval-error: {ex.Message}";
            Logger.LogWarning(ex, "[Conditional] Evaluate failed at step {StepId}: {Expr}", step.Id, conditionExpr);
        }

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
            // Even if evaluation failed, do not fail the workflow here.
            if (evalError != null) _workflowVariables["_last_conditional_error"] = evalError;
            return PrimitiveResult.Ok();
        }

        // 执行分支
        Logger.LogInformation("[DEBUG][Conditional] Executing branch with {Count} steps: [{Steps}]",
            branch.Count, string.Join(", ", branch.Select(s => s.Id)));

        PrimitiveResult? lastResult = null;
        for (int i = 0; i < branch.Count; i++)
        {
            var childStep = branch[i];
            Logger.LogInformation(
                "[DEBUG][Conditional] >>> Executing branch step {Index}/{Total}: '{StepId}' (type={Type})",
                i + 1, branch.Count, childStep.Id, childStep.Type);

            lastResult = await ExecuteStepAsync(childStep);

            Logger.LogInformation(
                "[DEBUG][Conditional] <<< Step '{StepId}' result: Success={Success}, Error={Error}, Value is null={IsNull}",
                childStep.Id, lastResult.Success, lastResult.Error ?? "none", lastResult.Value == null);

            if (!lastResult.Success)
            {
                Logger.LogWarning("[DEBUG][Conditional] ⚠️ Step '{StepId}' failed, breaking out of branch",
                    childStep.Id);
                break;
            }

            if (!string.IsNullOrEmpty(childStep.Store) && lastResult.Value != null)
            {
                _workflowVariables[childStep.Store] = lastResult.Value;
                Logger.LogInformation("[DEBUG][Conditional] Stored '{Store}' = {Type}",
                    childStep.Store, lastResult.Value.GetType().FullName);
            }
        }

        Logger.LogInformation("[DEBUG][Conditional] Branch execution completed");
        // If we had an eval error but branch executed successfully, keep the workflow moving.
        if (evalError != null && lastResult is { Success: true })
        {
            _workflowVariables["_last_conditional_error"] = evalError;
            // Preserve success & value; attach debug info through variables (visible in logs if needed)
            return PrimitiveResult.Ok(lastResult.Value, lastResult.TokensUsed, lastResult.LlmCalls);
        }
        if (evalError != null) _workflowVariables["_last_conditional_error"] = evalError;
        return lastResult ?? PrimitiveResult.Ok(null);
    }

    private async Task<PrimitiveResult> ExecuteVoteAsync(StepDefinition step)
    {
        var k = ResolveIntParameter(step.Parameters, "k", 3);
        var maxRounds = ResolveIntParameter(step.Parameters, "max_rounds", 10);
        var similarity = ResolveFloatParameter(step.Parameters, "similarity", _semanticSimilarityThreshold);
        var generator = step.Generator;

        if (generator == null)
        {
            return PrimitiveResult.Fail("vote requires 'generator' definition");
        }

        // ─────────────────────────────────────────────
        //  Red-Flag 策略（可插拔）
        //  优先级：步骤配置 > Coordinator 默认配置 > null (禁用)
        // ─────────────────────────────────────────────
        var redFlagStrategy = ResolveRedFlagStrategy(step.Parameters);
        var redFlagCount = 0;
        var maxRedFlags = ResolveIntParameter(step.Parameters, "max_red_flags", maxRounds * 2);

        // ─────────────────────────────────────────────
        //  使用 MAKER 的语义聚类 VoteEngine
        //  如果没有配置 embedding generator，自动回退到精确匹配
        // ─────────────────────────────────────────────
        using var engine = new VoteEngine(
            k,
            _embeddingGenerator, // null 时自动回退到精确匹配
            maxRounds,
            similarity);

        var useSemanticClustering = _embeddingGenerator != null;
        Logger.LogDebug(
            "Vote step {StepId}: K={K}, maxRounds={MaxRounds}, semantic={Semantic}, redFlag={RedFlag}",
            step.Id, k, maxRounds, useSemanticClustering, redFlagStrategy?.GetType().Name ?? "disabled");

        var totalTokens = 0;
        var totalCalls = 0;
        VoteResult? consensusResult = null;
        var round = 0;
        // 记录达成共识的轮次（用于 UI 与日志一致性）
        int? consensusReachedAtRound = null;

        // 并行批次大小：每批同时生成 N 个提案（受 Worker 数量和 K 值限制）
        var batchSize = Math.Max(1, Math.Min(k + 1, _workerIds.Count > 0 ? _workerIds.Count : 3));

        Console.WriteLine($"[VOTE] batchSize={batchSize}, k={k}, k+1={k + 1}, _workerIds.Count={_workerIds.Count}");

        while (consensusResult == null && round < maxRounds)
        {
            // 红旗过多时提前终止
            if (redFlagCount >= maxRedFlags)
            {
                Logger.LogWarning(
                    "Vote step {StepId}: Too many red flags ({RedFlags}), terminating early",
                    step.Id, redFlagCount);
                break;
            }

            var batchRound = round / batchSize + 1;

            // 发送投票进度事件
            EmitStepEvent(step, StepStatus.Running,
                $"Voting batch {batchRound}, generating {batchSize} proposals in parallel",
                progress: (float)round / maxRounds,
                voteRound: round, voteMaxRounds: maxRounds, voteK: k,
                parallelTotal: batchSize, parallelCompleted: 0);

            // 预渲染 prompt（所有并行任务共用）
            var genPrompt = generator.Parameters.GetValueOrDefault("prompt")?.ToString() ?? "";
            var genSystem = generator.Parameters.GetValueOrDefault("system")?.ToString();

            // DEBUG: 详细诊断 generator 和变量
            Console.WriteLine($"[DEBUG][Vote {step.Id}] ───────────────────────────────");
            Console.WriteLine($"[DEBUG][Vote {step.Id}] generator.Type: {generator.Type}");
            Console.WriteLine(
                $"[DEBUG][Vote {step.Id}] generator.Parameters keys: [{string.Join(", ", generator.Parameters.Keys)}]");
            Console.WriteLine($"[DEBUG][Vote {step.Id}] genPrompt length: {genPrompt.Length}");
            Console.WriteLine(
                $"[DEBUG][Vote {step.Id}] genPrompt first 200 chars: {(genPrompt.Length > 200 ? genPrompt[..200] : genPrompt)}");
            Console.WriteLine(
                $"[DEBUG][Vote {step.Id}] _workflowVariables keys: [{string.Join(", ", _workflowVariables.Keys)}]");

            foreach (var (key, value) in _workflowVariables)
            {
                var valStr = value?.ToString() ?? "null";
                var preview = valStr.Length > 100 ? valStr[..100] + "..." : valStr;
                Console.WriteLine($"[DEBUG][Vote {step.Id}] var[{key}] ({value?.GetType().Name ?? "null"}): {preview}");
            }

            Console.WriteLine($"[DEBUG][Vote {step.Id}] ───────────────────────────────");

            // ============================================================
            //  关键诊断：compose 卡住时，最常见是“模板渲染”或“LLM 首 token”卡住
            //  这里先把模板渲染耗时打出来，便于定位是否卡在 Render()
            // ============================================================
            var renderSw = System.Diagnostics.Stopwatch.StartNew();
            Logger.LogInformation("[VOTE] {StepId}: Rendering generator template...", step.Id);
            genPrompt = _templateEngine.Render(genPrompt, _workflowVariables);
            if (genSystem != null) genSystem = _templateEngine.Render(genSystem, _workflowVariables);
            renderSw.Stop();
            Logger.LogInformation("[VOTE] {StepId}: Template rendered in {Ms}ms, promptLen={Len}",
                step.Id, renderSw.ElapsedMilliseconds, genPrompt.Length);

            // ─────────────────────────────────────────────
            //  并行生成提案：同时启动多个 LLM 调用
            //  ⚡ 关键：先发送所有开始事件，实现视觉并行
            // ─────────────────────────────────────────────
            var batchTasks = new List<(int index, StepDefinition step, Task<PrimitiveResult> task)>();

            Console.WriteLine($"[VOTE] Generating batch: round={round}, batchSize={batchSize}, maxRounds={maxRounds}");
            for (int i = 0; i < batchSize && round + i < maxRounds; i++)
            {
                var proposalIndex = round + i + 1;
                var genStepId = $"{step.Id}.gen[{proposalIndex}]";
                Console.WriteLine($"[VOTE] Creating proposal {proposalIndex}: {genStepId}");
                var genStep = new StepDefinition { Id = genStepId, Type = "llm_call" };

                // 发送开始事件（所有卡片同时出现）
                // parallelTotal = batchSize，确保前端能正确计算 workerCount
                EmitStepEvent(genStep, StepStatus.Running,
                    $"Generating proposal #{proposalIndex}",
                    progress: 0,
                    parentStepId: step.Id,
                    parallelTotal: batchSize,
                    systemPrompt: genSystem,
                    userPrompt: genPrompt);

                // 启动 LLM 调用（不 await）
                var task = ExecuteLlmCallWithStreamingAsync(generator, genStep, genSystem, genPrompt);
                batchTasks.Add((proposalIndex, genStep, task));
            }

            // 等待所有并行任务完成
            var results = await Task.WhenAll(batchTasks.Select(t => t.task));

            // 处理结果
            for (int i = 0; i < results.Length; i++)
            {
                round++;
                var (proposalIndex, genStep, _) = batchTasks[i];
                var result = results[i];

                totalTokens += result.TokensUsed;
                totalCalls += result.LlmCalls;

                // 发送完成事件
                EmitStepEvent(genStep, result.Success ? StepStatus.Completed : StepStatus.Failed,
                    result.Success ? $"Proposal #{proposalIndex} generated" : $"Generation failed: {result.Error}",
                    progress: 1,
                    parentStepId: step.Id,
                    parallelTotal: batchSize,
                    systemPrompt: result.SystemPrompt,
                    userPrompt: result.UserPrompt,
                    assistantResponse: result.AssistantResponse);

                if (!result.Success) continue;

                // ✅ 已达成共识：仍然要把本批次剩余提案的完成事件都发出去（否则 UI 会出现某个 worker 永远空/卡住），
                // 但不再继续投票消耗。
                if (consensusResult != null) continue;

                // 使用原始 LLM 响应进行投票（不是解析后的对象）
                // VoteEngine 需要原始字符串来做语义聚类
                var proposal = result.AssistantResponse ?? result.Value?.ToString() ?? "";

                // Red-Flag 验证
                if (redFlagStrategy != null)
                {
                    var proposalId = $"{step.Id}.round{proposalIndex}";
                    if (!redFlagStrategy.Validate(proposal, proposalId, out var reason))
                    {
                        redFlagCount++;
                        Logger.LogWarning("🚩 Red flag in {StepId} round {Round}: {Reason}",
                            step.Id, proposalIndex, reason);

                        EmitStepEvent(step, StepStatus.Running,
                            $"🚩 Round {proposalIndex}: {reason}",
                            progress: (float)round / maxRounds,
                            voteRound: proposalIndex, voteMaxRounds: maxRounds, voteK: k,
                            redFlagReason: reason);
                        continue;
                    }
                }

                // 提交投票
                consensusResult = await engine.SubmitVoteAsync(proposal);
                if (consensusResult != null && consensusReachedAtRound == null)
                {
                    // 保留“首次达成共识”的轮次，不被后续（未投票的）提案影响
                    consensusReachedAtRound = proposalIndex;
                }
            }

            // 发送当前投票状态
            var currentVotes = consensusResult?.LeaderVotes ?? 0;
            var displayRound = consensusReachedAtRound ?? round;
            EmitStepEvent(step, StepStatus.Running,
                $"Round {displayRound}: {currentVotes}/{k} votes" + (redFlagCount > 0 ? $" (🚩{redFlagCount})" : ""),
                progress: (float)round / maxRounds,
                voteRound: displayRound, voteMaxRounds: maxRounds, voteK: k,
                voteCurrentVotes: currentVotes);

            if (consensusResult != null)
            {
                Logger.LogInformation(
                    "✓ Vote consensus reached at round {Round}: {LeaderVotes}/{K} votes (🚩{RedFlags})",
                    displayRound, consensusResult.LeaderVotes, k, redFlagCount);
            }
        }

        // 添加 embedding 调用计数
        var embeddingCalls = engine.EmbeddingCallCount;

        // 获取原始内容
        string rawContent;
        if (consensusResult != null && consensusResult.Success)
        {
            rawContent = consensusResult.WinningContent;
        }
        else
        {
            // 没有达成共识，返回得票最多的
            var bestCandidate = engine.GetBestCandidate();
            rawContent = bestCandidate?.Content ?? "";

            Logger.LogWarning(
                "Vote step {StepId}: No consensus after {Rounds} rounds (🚩{RedFlags}), using best candidate ({Votes} votes)",
                step.Id, maxRounds, redFlagCount, bestCandidate?.Votes ?? 0);
        }

        // ─────────────────────────────────────────────
        //  关键修复：根据 generator.output 解析结果
        //  例如 output: json_array 应该返回 List<object>
        // ─────────────────────────────────────────────
        var outputType = generator.Parameters.GetValueOrDefault("output")?.ToString() ?? "text";

        // DEBUG: 检查 rawContent
        Logger.LogInformation("[DEBUG][Vote] rawContent length: {Len}", rawContent?.Length ?? 0);
        Logger.LogInformation("[DEBUG][Vote] rawContent preview: {Preview}",
            rawContent?.Length > 500 ? rawContent[..500] : rawContent);
        Logger.LogInformation("[DEBUG][Vote] outputType: {Type}", outputType);

        var parsedResult = ParseOutput(rawContent, outputType);
        if (parsedResult == null)
        {
            return PrimitiveResult.Fail("redflag-parse-null");
        }

        // DEBUG: 检查解析结果
        Logger.LogInformation("[DEBUG][Vote] parsedResult is null: {IsNull}", parsedResult == null);
        if (parsedResult != null)
        {
            Logger.LogInformation("[DEBUG][Vote] parsedResult type: {Type}", parsedResult.GetType().FullName);
            if (parsedResult is System.Collections.IList list)
            {
                Logger.LogInformation("[DEBUG][Vote] parsedResult is List with {Count} items", list.Count);
            }
        }

        // ============================================================
        //  将“最终共识内容”透出到步骤事件（AssistantResponse）
        //
        //  WHY:
        //  - vote step 本身不是 llm_call，它的 PrimitiveResult 默认不会携带 AssistantResponse
        //  - 这会导致上层 UI 只能看到 proposals，却不知道最终共识选择了哪一个
        //  - PaperReview 需要把共识结论绑定到 atomic point 上进行可视化
        // ============================================================
        return new PrimitiveResult
        {
            Success = true,
            Value = parsedResult,
            TokensUsed = totalTokens,
            LlmCalls = totalCalls + embeddingCalls,
            AssistantResponse = rawContent
        };
    }

    /// <summary>
    /// 根据输出类型解析 LLM 返回的内容
    /// </summary>
    private object? ParseOutput(string content, string outputType)
    {
        var factory = new OutputParserFactory();
        var parser = factory.Create(outputType);
        try
        {
            return parser.Parse(content);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "ParseOutput failed for type {Type}", outputType);
            return null;
        }
    }

    /// <summary>
    /// 解析步骤级别的 Red-Flag 策略
    /// DSL 语法：
    ///   red_flag: english    # 使用英文策略
    ///   red_flag: chinese    # 使用中文策略
    ///   red_flag: code       # 使用代码策略
    ///   red_flag: false      # 禁用
    ///   red_flag: true       # 使用 Coordinator 默认配置
    ///   (不写)                # 使用 Coordinator 默认配置
    /// </summary>
    private IRedFlagStrategy? ResolveRedFlagStrategy(Dictionary<string, object?> parameters)
    {
        var redFlagConfig = parameters.GetValueOrDefault("red_flag");

        return redFlagConfig switch
        {
            // 显式禁用
            false or "false" or "none" or "disabled" => null,

            // 显式启用（使用默认配置）
            true or "true" or "default" => _redFlagStrategy,

            // 指定策略名称
            "english" => new DefaultEnglishRedFlagStrategy(),
            "chinese" => new ChineseRedFlagStrategy(),
            "code" => new CodeAwareRedFlagStrategy(),

            // 嵌套配置对象
            Dictionary<string, object?> config => ResolveRedFlagFromConfig(config),

            // 未配置：使用 Coordinator 默认
            null => _redFlagStrategy,

            // 其他：尝试解析为策略名
            string name => ResolveRedFlagByName(name),

            _ => _redFlagStrategy
        };
    }

    private IRedFlagStrategy? ResolveRedFlagFromConfig(Dictionary<string, object?> config)
    {
        var enabled = config.GetValueOrDefault("enabled");
        if (enabled is false or "false")
        {
            return null;
        }

        var strategyName = config.GetValueOrDefault("strategy")?.ToString() ?? "english";
        var options = new RedFlagOptions
        {
            MinContentLength = config.TryGetValue("min_length", out var min) && min != null
                ? Convert.ToInt32(min)
                : 10,
            MaxContentLength = config.TryGetValue("max_length", out var max) && max != null
                ? Convert.ToInt32(max)
                : 8000,
            EnableRefusalDetection = config.GetValueOrDefault("detect_refusal") is not (false or "false"),
            EnableDegenerationDetection = config.GetValueOrDefault("detect_degeneration") is not (false or "false"),
            EnableLengthValidation = config.GetValueOrDefault("validate_length") is not (false or "false")
        };

        return strategyName.ToLowerInvariant() switch
        {
            "english" => new DefaultEnglishRedFlagStrategy(options),
            "chinese" => new ChineseRedFlagStrategy(options),
            "code" => new CodeAwareRedFlagStrategy(options),
            "none" or "disabled" => null,
            _ => new DefaultEnglishRedFlagStrategy(options)
        };
    }

    private IRedFlagStrategy? ResolveRedFlagByName(string name) => name.ToLowerInvariant() switch
    {
        "english" => new DefaultEnglishRedFlagStrategy(),
        "chinese" => new ChineseRedFlagStrategy(),
        "code" => new CodeAwareRedFlagStrategy(),
        "none" or "disabled" or "false" => null,
        _ => _redFlagStrategy
    };

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
            float f => (int)f,
            string s when int.TryParse(s, out var parsed) => parsed,
            string s => ConvertToInt(_templateEngine.Evaluate(s, _workflowVariables), defaultValue),
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
            long l => l,
            string s when float.TryParse(s, out var parsed) => parsed,
            string s => ConvertToFloat(_templateEngine.Evaluate(s, _workflowVariables), defaultValue),
            _ => defaultValue
        };
    }

    /// <summary>
    /// 安全转换为 int，处理 object unboxing
    /// </summary>
    private static int ConvertToInt(object? value, int defaultValue) => value switch
    {
        int i => i,
        long l => (int)l,
        double d => (int)d,
        float f => (int)f,
        decimal m => (int)m,
        string s when int.TryParse(s, out var parsed) => parsed,
        _ => defaultValue
    };

    /// <summary>
    /// 安全转换为 float，处理 object unboxing
    /// </summary>
    private static float ConvertToFloat(object? value, float defaultValue) => value switch
    {
        float f => f,
        double d => (float)d,
        int i => i,
        long l => l,
        decimal m => (float)m,
        string s when float.TryParse(s, out var parsed) => parsed,
        _ => defaultValue
    };

    private async Task<PrimitiveResult> ExecuteWorkflowCallAsync(StepDefinition step)
    {
        var workflowName = step.Workflow ?? "";
        var workflow = _workflowRegistry.Get(workflowName);

        if (workflow == null)
        {
            return PrimitiveResult.Fail($"Workflow '{workflowName}' not found");
        }

        // ============================================================
        //  对单次 workflow_call 允许覆盖 max_depth
        //
        //  WHY:
        //  - YAML 既支持 workflow 输入 max_depth，也可能在 workflow_call.params 里传 max_depth
        //  - Coordinator 内部执行 workflow_call 不走 StartWorkflowRequest，因此需要在这里显式处理
        // ============================================================
        var savedMaxDepth = CustomState.MaxDepth;

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
                        var resolved = _templateEngine.ResolveValue(value, _workflowVariables);
                        if (resolved != null)
                            childVariables[key] = resolved;
                    }
                }
            }

            // 允许子调用用 params.max_depth 覆盖（仅对本次调用生效）
            if (TryGetPositiveInt(childVariables, "max_depth", out var callMaxDepth))
            {
                CustomState.MaxDepth = Math.Clamp(callMaxDepth, 1, 200);
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

            // DEBUG: 检查子工作流输出
            Logger.LogInformation("[DEBUG][WorkflowCall] _output is null: {IsNull}, type: {Type}",
                output == null, output?.GetType().FullName ?? "null");
            if (output is Dictionary<string, object?> dict)
            {
                foreach (var (k, v) in dict)
                {
                    Logger.LogInformation("[DEBUG][WorkflowCall] _output[{Key}] = {Value}",
                        k, v?.ToString()?.Substring(0, Math.Min(100, v?.ToString()?.Length ?? 0)) ?? "null");
                }
            }

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
            // 恢复本次调用前的 maxDepth，避免污染外层流程
            CustomState.MaxDepth = savedMaxDepth;
            CustomState.CurrentDepth--;
        }
    }

    // ============================================================
    //  小工具：从变量表里解析正整数
    // ============================================================
    private static bool TryGetPositiveInt(
        Dictionary<string, object> variables,
        string key,
        out int value)
    {
        value = 0;
        if (!variables.TryGetValue(key, out var raw) || raw == null) return false;

        try
        {
            value = raw switch
            {
                int i => i,
                long l => (int)l,
                float f => (int)f,
                double d => (int)d,
                decimal m => (int)m,
                string s when int.TryParse(s, out var parsed) => parsed,
                _ => Convert.ToInt32(raw)
            };
        }
        catch
        {
            value = 0;
            return false;
        }

        return value > 0;
    }

    private Task<PrimitiveResult> ExecuteCheckpointAsync(StepDefinition step)
    {
        Logger.LogDebug("Checkpoint at step: {StepId}", step.Id);
        return Task.FromResult(PrimitiveResult.Ok(null));
    }

    // ============================================================
    //  assign: variable copy / projection
    //
    //  WHY:
    //  - workflow_call returns an object (dictionary) but DSL lacks a cheap "set var" primitive.
    //  - for theorem-loop recursion we need: state = recursive_output.state (without an extra LLM call).
    //
    //  DSL:
    //    - id: unwrap
    //      type: assign
    //      from: "recursive_output.state"
    //      store: state
    // ============================================================
    private Task<PrimitiveResult> ExecuteAssignAsync(StepDefinition step)
    {
        var from = step.Parameters.GetValueOrDefault("from")?.ToString();
        if (string.IsNullOrWhiteSpace(from))
            return Task.FromResult(PrimitiveResult.Fail("assign requires 'from'"));

        if (string.IsNullOrWhiteSpace(step.Store))
            return Task.FromResult(PrimitiveResult.Fail("assign requires 'store' as target variable name"));

        var value = ResolvePathValue(_workflowVariables, from!);
        if (value == null)
            return Task.FromResult(PrimitiveResult.Fail($"assign source '{from}' resolved to null"));

        _workflowVariables[step.Store!] = value;
        return Task.FromResult(PrimitiveResult.Ok(value));
    }

    private static object? ResolvePathValue(Dictionary<string, object> variables, string path)
    {
        // supports dotted paths: "a.b.c"
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;

        if (!variables.TryGetValue(parts[0], out var current) || current == null)
            return null;

        for (var i = 1; i < parts.Length; i++)
        {
            var key = parts[i];
            current = current switch
            {
                IDictionary<string, object> dict => dict.TryGetValue(key, out var v) ? v : null,
                System.Collections.IDictionary nd => nd.Contains(key) ? nd[key] : null,
                _ => null
            };
            if (current == null) return null;
        }

        return current;
    }

    // ============================================================
    //  辅助方法
    // ============================================================

    private async Task FailExecutionAsync(string error)
    {
        CustomState.Status = ExecutionStatus.EsFailed;
        CustomState.CurrentPhase = "Failed";
        CustomState.Error = error;

        // IMPORTANT:
        // - 之前这里不打日志，导致“后端没有报错但系统停了”的错觉
        // - 失败必须在日志里可见（至少包含 executionId / 当前 step）
        Logger.LogError("[WORKFLOW] Failed (executionId={ExecutionId}, step={StepId}, phase={Phase}): {Error}",
            CustomState.ExecutionId, CustomState.CurrentStepId, CustomState.CurrentPhase, error);

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

    // Protobuf 转换：使用 ProtoValueConverter 工具类

    // ============================================================
    //  步骤事件追踪
    // ============================================================

    private void EmitStepEvent(
        StepDefinition step,
        StepStatus status,
        string? message = null,
        float progress = 0,
        int voteRound = 0,
        int voteMaxRounds = 0,
        int voteK = 0,
        int voteCurrentVotes = 0,
        int parallelTotal = 0,
        int parallelCompleted = 0,
        int parallelFailed = 0,
        string? parentStepId = null,
        // LLM 对话记录
        string? systemPrompt = null,
        string? userPrompt = null,
        string? assistantResponse = null,
        // Red-Flag 信息
        string? redFlagReason = null)
    {
        var now = DateTime.UtcNow;
        var durationMs = 0;

        if (status == StepStatus.Running)
        {
            _stepStartTimes[step.Id] = now;
        }
        else if (_stepStartTimes.TryGetValue(step.Id, out var startTime))
        {
            durationMs = (int)(now - startTime).TotalMilliseconds;
        }

        var evt = new WorkflowStepEvent
        {
            RunId = CustomState.ExecutionId ?? "",
            WorkflowName = CustomState.WorkflowName ?? "",
            StepId = step.Id,
            StepType = step.Type,
            Status = status,
            Progress = progress,
            Message = message ?? GetDefaultMessage(step, status),
            Timestamp = Timestamp.FromDateTime(now),
            ParentStepId = parentStepId ?? "",
            Depth = CustomState.CurrentDepth,
            VoteRound = voteRound,
            VoteMaxRounds = voteMaxRounds,
            VoteK = voteK,
            VoteCurrentVotes = voteCurrentVotes,
            ParallelTotal = parallelTotal,
            ParallelCompleted = parallelCompleted,
            ParallelFailed = parallelFailed,
            DurationMs = durationMs,
            LlmCalls = CustomState.TotalLlmCalls,
            TokensUsed = CustomState.TotalTokensUsed,
            // LLM 对话记录
            SystemPrompt = systemPrompt ?? "",
            UserPrompt = userPrompt ?? "",
            AssistantResponse = assistantResponse ?? "",
            // Red-Flag 信息
            RedFlagReason = redFlagReason ?? ""
        };

        // 多任务并行（vote streaming）时，避免 List 并发写导致内存损坏/卡死
        lock (_stepEventsLock)
        {
            _stepEvents.Add(evt);
        }
        _onStepEvent?.Invoke(evt);

        Logger.LogDebug("[Workflow] Step {StepId} ({Type}): {Status} - {Message}",
            step.Id, step.Type, status, message);
    }

    private static string GetDefaultMessage(StepDefinition step, StepStatus status)
    {
        return status switch
        {
            StepStatus.Pending => $"Step '{step.Id}' pending",
            StepStatus.Running => $"Executing {step.Type}: {step.Id}",
            StepStatus.Completed => $"Step '{step.Id}' completed",
            StepStatus.Failed => $"Step '{step.Id}' failed",
            StepStatus.Skipped => $"Step '{step.Id}' skipped",
            _ => $"Step '{step.Id}' - {status}"
        };
    }
}