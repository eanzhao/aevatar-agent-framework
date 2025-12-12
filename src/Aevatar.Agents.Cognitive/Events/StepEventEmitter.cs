using System.Collections.Concurrent;
using Aevatar.Agents.Cognitive.Messages;
using Google.Protobuf.WellKnownTypes;

using StepDefinition = Aevatar.Agents.Cognitive.Primitives.StepDefinition;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Cognitive.Events;

// ============================================================
//  步骤事件发射器
//  职责：统一管理工作流步骤事件的创建与发送
// ============================================================

/// <summary>
/// 步骤事件回调委托。
/// </summary>
public delegate void StepEventHandler(WorkflowStepEvent evt);

/// <summary>
/// 步骤事件发射器 - 统一管理事件创建与回调。
/// </summary>
public sealed class StepEventEmitter
{
    private readonly List<WorkflowStepEvent> _events = [];
    private readonly ConcurrentDictionary<string, DateTime> _startTimes = new();
    private readonly ILogger _logger;
    
    private string _runId = "";
    private string _workflowName = "";
    private int _currentDepth;
    private int _totalLlmCalls;
    private int _totalTokens;
    
    public event StepEventHandler? OnStepEvent;
    
    public IReadOnlyList<WorkflowStepEvent> Events => _events;
    
    public StepEventEmitter(ILogger logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// 设置运行上下文。
    /// </summary>
    public void SetContext(string runId, string workflowName, int depth = 0)
    {
        _runId = runId;
        _workflowName = workflowName;
        _currentDepth = depth;
    }
    
    /// <summary>
    /// 更新统计信息。
    /// </summary>
    public void UpdateStats(int llmCalls, int tokens)
    {
        _totalLlmCalls = llmCalls;
        _totalTokens = tokens;
    }
    
    /// <summary>
    /// 发送步骤事件。
    /// </summary>
    public void Emit(StepEventParams p)
    {
        var now = DateTime.UtcNow;
        var durationMs = 0;
        
        if (p.Status == StepStatus.Running)
        {
            _startTimes[p.StepId] = now;
        }
        else if (_startTimes.TryGetValue(p.StepId, out var startTime))
        {
            durationMs = (int)(now - startTime).TotalMilliseconds;
        }
        
        var evt = new WorkflowStepEvent
        {
            RunId = _runId,
            WorkflowName = _workflowName,
            StepId = p.StepId,
            StepType = p.StepType,
            Status = p.Status,
            Progress = p.Progress,
            Message = p.Message ?? GetDefaultMessage(p.StepId, p.StepType, p.Status),
            Timestamp = Timestamp.FromDateTime(now),
            ParentStepId = p.ParentStepId ?? "",
            Depth = _currentDepth,
            VoteRound = p.VoteRound,
            VoteMaxRounds = p.VoteMaxRounds,
            VoteK = p.VoteK,
            VoteCurrentVotes = p.VoteCurrentVotes,
            ParallelTotal = p.ParallelTotal,
            ParallelCompleted = p.ParallelCompleted,
            ParallelFailed = p.ParallelFailed,
            DurationMs = durationMs,
            LlmCalls = _totalLlmCalls,
            TokensUsed = _totalTokens,
            SystemPrompt = p.SystemPrompt ?? "",
            UserPrompt = p.UserPrompt ?? "",
            AssistantResponse = p.AssistantResponse ?? "",
            RedFlagReason = p.RedFlagReason ?? ""
        };
        
        _events.Add(evt);
        OnStepEvent?.Invoke(evt);
        
        _logger.LogDebug("[Workflow] Step {StepId} ({Type}): {Status} - {Message}",
            p.StepId, p.StepType, p.Status, p.Message);
    }
    
    /// <summary>
    /// 快捷方法：发送步骤开始事件。
    /// </summary>
    public void EmitRunning(StepDefinition step, string? message = null, string? parentStepId = null,
        string? systemPrompt = null, string? userPrompt = null)
    {
        Emit(new StepEventParams
        {
            StepId = step.Id,
            StepType = step.Type,
            Status = StepStatus.Running,
            Message = message,
            ParentStepId = parentStepId,
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt
        });
    }
    
    /// <summary>
    /// 快捷方法：发送步骤完成事件。
    /// </summary>
    public void EmitCompleted(StepDefinition step, string? message = null, string? parentStepId = null,
        string? systemPrompt = null, string? userPrompt = null, string? assistantResponse = null)
    {
        Emit(new StepEventParams
        {
            StepId = step.Id,
            StepType = step.Type,
            Status = StepStatus.Completed,
            Progress = 1.0f,
            Message = message,
            ParentStepId = parentStepId,
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt,
            AssistantResponse = assistantResponse
        });
    }
    
    /// <summary>
    /// 快捷方法：发送步骤失败事件。
    /// </summary>
    public void EmitFailed(StepDefinition step, string error, string? parentStepId = null)
    {
        Emit(new StepEventParams
        {
            StepId = step.Id,
            StepType = step.Type,
            Status = StepStatus.Failed,
            Message = error,
            ParentStepId = parentStepId
        });
    }
    
    /// <summary>
    /// 快捷方法：发送 fan-out 进度事件。
    /// </summary>
    public void EmitFanOutProgress(StepDefinition step, int completed, int failed, int total, string? message = null)
    {
        Emit(new StepEventParams
        {
            StepId = step.Id,
            StepType = step.Type,
            Status = StepStatus.Running,
            Progress = total > 0 ? (float)completed / total : 0,
            Message = message ?? $"Progress: {completed}/{total} (failed: {failed})",
            ParallelTotal = total,
            ParallelCompleted = completed,
            ParallelFailed = failed
        });
    }
    
    /// <summary>
    /// 快捷方法：发送投票进度事件。
    /// </summary>
    public void EmitVoteProgress(StepDefinition step, int round, int maxRounds, int k, int currentVotes,
        string? message = null, string? redFlagReason = null)
    {
        Emit(new StepEventParams
        {
            StepId = step.Id,
            StepType = step.Type,
            Status = StepStatus.Running,
            Progress = maxRounds > 0 ? (float)round / maxRounds : 0,
            Message = message ?? $"Voting round {round}/{maxRounds}",
            VoteRound = round,
            VoteMaxRounds = maxRounds,
            VoteK = k,
            VoteCurrentVotes = currentVotes,
            RedFlagReason = redFlagReason
        });
    }
    
    private static string GetDefaultMessage(string stepId, string stepType, StepStatus status)
    {
        return status switch
        {
            StepStatus.Pending => $"Step '{stepId}' pending",
            StepStatus.Running => $"Executing {stepType}: {stepId}",
            StepStatus.Completed => $"Step '{stepId}' completed",
            StepStatus.Failed => $"Step '{stepId}' failed",
            StepStatus.Skipped => $"Step '{stepId}' skipped",
            _ => $"Step '{stepId}' - {status}"
        };
    }
}

/// <summary>
/// 步骤事件参数。
/// </summary>
public sealed class StepEventParams
{
    public required string StepId { get; init; }
    public required string StepType { get; init; }
    public required StepStatus Status { get; init; }
    public float Progress { get; init; }
    public string? Message { get; init; }
    public string? ParentStepId { get; init; }
    
    // 投票相关
    public int VoteRound { get; init; }
    public int VoteMaxRounds { get; init; }
    public int VoteK { get; init; }
    public int VoteCurrentVotes { get; init; }
    
    // 并行相关
    public int ParallelTotal { get; init; }
    public int ParallelCompleted { get; init; }
    public int ParallelFailed { get; init; }
    
    // LLM 对话记录
    public string? SystemPrompt { get; init; }
    public string? UserPrompt { get; init; }
    public string? AssistantResponse { get; init; }
    
    // Red-Flag
    public string? RedFlagReason { get; init; }
}
