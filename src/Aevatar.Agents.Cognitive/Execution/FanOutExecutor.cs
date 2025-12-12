using System.Collections.Concurrent;
using Aevatar.Agents.Cognitive.Messages;
using Aevatar.Agents.Cognitive.Template;
using Google.Protobuf.WellKnownTypes;

using StepDefinition = Aevatar.Agents.Cognitive.Primitives.StepDefinition;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Cognitive.Execution;

// ============================================================
//  Fan-Out 执行器
//  职责：管理并行任务的派发、收集、聚合
// ============================================================

/// <summary>
/// Fan-out 运行时状态。
/// </summary>
public sealed class FanOutState
{
    public ConcurrentDictionary<string, StepCompletedEventProto> Results { get; } = new();
    public ConcurrentDictionary<string, string> ChildTypes { get; } = new();
    public ConcurrentDictionary<string, string> UserPrompts { get; } = new();
    public ConcurrentDictionary<string, string> SystemPrompts { get; } = new();
    
    public int ExpectedCount { get; set; }
    public int SuccessCount;
    public TaskCompletionSource<bool>? CompletionSource { get; set; }
    public StepDefinition? CurrentStep { get; set; }
    
    public void Reset()
    {
        Results.Clear();
        ChildTypes.Clear();
        UserPrompts.Clear();
        SystemPrompts.Clear();
        ExpectedCount = 0;
        SuccessCount = 0;
        CompletionSource = null;
        CurrentStep = null;
    }
}

/// <summary>
/// Fan-out 子任务请求。
/// </summary>
public sealed record FanOutTask(
    string RequestId,
    string StepId,
    string StepType,
    Dictionary<string, object> Parameters,
    Dictionary<string, object> Variables,
    string? UserPrompt,
    string? SystemPrompt);

/// <summary>
/// Fan-out 执行器 - 处理并行任务的派发与收集。
/// </summary>
public sealed class FanOutExecutor
{
    private readonly TemplateEngine _templateEngine;
    private readonly ILogger _logger;
    private readonly FanOutState _state = new();
    
    public FanOutState State => _state;
    
    public FanOutExecutor(TemplateEngine templateEngine, ILogger logger)
    {
        _templateEngine = templateEngine;
        _logger = logger;
    }
    
    /// <summary>
    /// 准备 fan-out 执行，返回待派发的子任务列表。
    /// </summary>
    public IReadOnlyList<FanOutTask> Prepare(
        StepDefinition step,
        StepDefinition childStep,
        List<object> items,
        Dictionary<string, object> workflowVariables,
        string executionId)
    {
        _state.Reset();
        _state.ExpectedCount = items.Count;
        _state.CompletionSource = new TaskCompletionSource<bool>();
        _state.CurrentStep = step;
        
        var tasks = new List<FanOutTask>(items.Count);
        
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var requestId = $"{executionId}-{step.Id}-{i}";
            var stepId = $"{step.Id}[{i}]";
            
            // 构建子上下文
            var childVariables = new Dictionary<string, object>(workflowVariables)
            {
                ["item"] = item,
                ["index"] = i
            };
            
            // 渲染 prompt
            var userPrompt = RenderPrompt(childStep.Parameters, "prompt", childVariables);
            var systemPrompt = RenderPrompt(childStep.Parameters, "system", childVariables);
            
            // 缓存用于后续事件
            _state.ChildTypes[requestId] = childStep.Type;
            if (userPrompt != null) _state.UserPrompts[requestId] = userPrompt;
            if (systemPrompt != null) _state.SystemPrompts[requestId] = systemPrompt;
            
            tasks.Add(new FanOutTask(
                requestId,
                stepId,
                childStep.Type,
                new Dictionary<string, object>(childStep.Parameters),
                childVariables,
                userPrompt,
                systemPrompt));
        }
        
        _logger.LogInformation("FanOut prepared {Count} tasks", tasks.Count);
        return tasks;
    }
    
    /// <summary>
    /// 处理子任务完成事件，返回是否全部完成。
    /// </summary>
    public bool HandleCompletion(StepCompletedEventProto evt, out string? childType, out string? userPrompt, out string? systemPrompt)
    {
        _state.Results[evt.RequestId] = evt;
        _state.ChildTypes.TryGetValue(evt.RequestId, out childType);
        _state.UserPrompts.TryGetValue(evt.RequestId, out userPrompt);
        _state.SystemPrompts.TryGetValue(evt.RequestId, out systemPrompt);
        
        if (evt.Success)
        {
            Interlocked.Increment(ref _state.SuccessCount);
            _state.ChildTypes.TryRemove(evt.RequestId, out _);
        }
        
        var allDone = _state.SuccessCount >= _state.ExpectedCount;
        if (allDone)
        {
            _state.CurrentStep = null;
            _state.CompletionSource?.TrySetResult(true);
        }
        
        return allDone;
    }
    
    /// <summary>
    /// 获取当前进度信息。
    /// </summary>
    public (int completed, int failed, int total) GetProgress()
    {
        var failed = _state.Results.Values.Count(r => !r.Success && !string.IsNullOrEmpty(r.Error));
        return (_state.SuccessCount, failed, _state.ExpectedCount);
    }
    
    /// <summary>
    /// 收集并聚合结果。
    /// </summary>
    public (List<object> results, int totalTokens, int totalCalls) CollectResults(string reducer = "collect")
    {
        var results = _state.Results.Values
            .OrderBy(r => r.StepId)
            .Where(r => r.Success)
            .Select(r => (object)r.Result)
            .ToList();
        
        var totalTokens = _state.Results.Values.Sum(r => r.TokensUsed);
        var totalCalls = _state.Results.Values.Sum(r => r.LlmCalls);
        
        var reduced = ApplyReducer(results, reducer);
        return (reduced, totalTokens, totalCalls);
    }
    
    /// <summary>
    /// 等待所有任务完成。
    /// </summary>
    public async Task<bool> WaitAsync(TimeSpan timeout)
    {
        if (_state.CompletionSource == null) return false;
        
        var completed = await Task.WhenAny(
            _state.CompletionSource.Task,
            Task.Delay(timeout));
        
        return completed == _state.CompletionSource.Task;
    }
    
    // ─────────────────────────────────────────────────────────
    //  私有方法
    // ─────────────────────────────────────────────────────────
    
    private string? RenderPrompt(Dictionary<string, object?> parameters, string key, Dictionary<string, object> variables)
    {
        var template = parameters.GetValueOrDefault(key)?.ToString();
        return template != null ? _templateEngine.Render(template, variables) : null;
    }
    
    private static List<object> ApplyReducer(List<object> results, string reducer)
    {
        return reducer.ToLowerInvariant() switch
        {
            "flatten" => results.SelectMany(r => r switch
            {
                IEnumerable<object> list => list,
                System.Collections.IList list => list.Cast<object>(),
                _ => [r]
            }).ToList(),
            "first" => results.Take(1).ToList(),
            "last" => results.TakeLast(1).ToList(),
            _ => results // collect
        };
    }
}
