using Aevatar.AxiomReasoning.Models;
using ReasoningProgress = Aevatar.CognitiveMesh.Abstractions.ReasoningProgress;
using Microsoft.Extensions.Logging;

namespace Aevatar.AxiomReasoning.Services;

// ============================================================
//  EVENT BRIDGE
//  职责：ReasoningProgress → Session 状态 + SSE 事件
// ============================================================

public sealed class AxiomReasoningEventBridge
{
    private const int MaxPreviewChars = 600;
    private const int MaxBodyChars = 6000;

    private readonly ILogger<AxiomReasoningEventBridge> _logger;

    public AxiomReasoningEventBridge(ILogger<AxiomReasoningEventBridge> logger)
    {
        _logger = logger;
    }

    public void HandleProgress(AxiomSession session, ReasoningProgress p)
    {
        // 更新 session 基础状态（供 status API 使用）
        session.CurrentPhase = p.Phase;

        var percent = Math.Clamp((int)(p.ProgressPercent * 100), 0, 100);
        session.ProgressPercent = percent;

        // 引擎累计统计（如果有填充）
        if (p.TotalLlmCalls.HasValue) session.TotalLlmCalls = p.TotalLlmCalls.Value;

        if (p.TotalPromptTokens.HasValue || p.TotalCompletionTokens.HasValue)
        {
            session.TotalTokens = (p.TotalPromptTokens ?? 0) + (p.TotalCompletionTokens ?? 0);
        }

        // 时间线（压缩：只记录关键阶段变化）
        if (!string.IsNullOrWhiteSpace(p.Phase) && (session.Timeline.Count == 0 || session.Timeline[^1].Phase != p.Phase))
        {
            session.Timeline.Add(new TimelineEntry(p.Phase!, p.Message ?? "", DateTimeOffset.UtcNow));
        }

        // ─────────────────────────────────────────────
        //  可读内容抽取（避免把 token 流直接当日志刷屏）
        // ─────────────────────────────────────────────
        var stepStatus = p.StepStatus ?? "";
        var isCompleted = stepStatus.Contains("Completed", StringComparison.OrdinalIgnoreCase);
        var isFailed = stepStatus.Contains("Failed", StringComparison.OrdinalIgnoreCase);
        var includeBody = isCompleted || isFailed || string.Equals(p.StepType, "vote", StringComparison.OrdinalIgnoreCase);

        var assistant = p.AssistantResponse;
        var assistantPreview = Truncate(assistant, MaxPreviewChars);
        var assistantBody = includeBody ? Truncate(assistant, MaxBodyChars) : null;

        var sys = includeBody ? Truncate(p.SystemPrompt, 2000) : null;
        var user = includeBody ? Truncate(p.UserPrompt, 4000) : null;

        // 推送 SSE 事件（类似 PaperReview 的“可读事件”，而不是 token dump）
        session.EventChannel.Writer.TryWrite(new Aevatar.AxiomReasoning.Models.ProgressEvent
        {
            SessionId = session.Id,
            Phase = p.Phase,
            Message = p.Message,
            ProgressPercent = percent,

            WorkerId = p.TaskId,
            Depth = p.Depth,
            StepId = p.StepId,
            StepType = p.StepType,
            StepStatus = p.StepStatus,

            SystemPrompt = sys,
            UserPrompt = user,
            AssistantResponsePreview = assistantPreview,
            AssistantResponse = assistantBody,

            VoteRound = p.VoteRound ?? 0,
            VoteMaxRounds = p.VoteMaxRounds ?? 0,
            VoteK = p.VoteK ?? 0,
            VoteCurrentVotes = p.VoteCurrentVotes ?? 0,

            ParallelTotal = p.ParallelTotal ?? 0,
            ParallelCompleted = p.ParallelCompleted ?? 0,
            ParallelFailed = p.ParallelFailed ?? 0,

            TotalLlmCalls = session.TotalLlmCalls,
            TotalTokens = session.TotalTokens
        });

        _logger.LogDebug("[AXIOM] {Session} {Phase} {Step} {Status}",
            session.Id, p.Phase, p.StepId, p.StepStatus);
    }

    private static string? Truncate(string? s, int maxChars)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (s.Length <= maxChars) return s;
        return s[..maxChars] + "\n…(truncated)…";
    }
}


