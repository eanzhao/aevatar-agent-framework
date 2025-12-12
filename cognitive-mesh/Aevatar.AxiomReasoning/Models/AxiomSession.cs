using System.Collections.Concurrent;
using System.Threading.Channels;
using Aevatar.CognitiveMesh.Abstractions;

namespace Aevatar.AxiomReasoning.Models;

// ============================================================
//  AXIOM SESSION
//  公理推理会话模型（仿照 PaperReview 的 Session + SSE）
// ============================================================

public enum AxiomSessionStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// 公理推理会话。
/// </summary>
public sealed class AxiomSession
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public AxiomSessionStatus Status { get; set; } = AxiomSessionStatus.Pending;

    // 用户输入（原始）
    public string AxiomsText { get; set; } = "";
    public string Goal { get; set; } = "";

    // 共识参数
    public int K { get; set; } = 3;
    public int MaxRounds { get; set; } = 10;
    public int MaxDepth { get; set; } = 10;

    // 运行统计
    public int ProgressPercent { get; set; }
    public string CurrentPhase { get; set; } = "";
    public int TotalLlmCalls { get; set; }
    public long TotalTokens { get; set; }
    public TimeSpan Duration { get; set; }

    // 引擎结果
    public ReasoningResult? Result { get; set; }
    public string? Error { get; set; }

    // 时间线
    public List<TimelineEntry> Timeline { get; } = [];

    // SSE 事件通道
    public Channel<AxiomEvent> EventChannel { get; } = Channel.CreateUnbounded<AxiomEvent>();

    public CancellationTokenSource CancellationTokenSource { get; } = new();

    // 产出文件（内存 + 可选落盘）
    public ConcurrentDictionary<string, ConcurrentDictionary<string, string>> Files { get; } = new();
    public string? OutputDir { get; set; }
}

public record TimelineEntry(string Phase, string Message, DateTimeOffset Timestamp);

// ============================================================
//  SSE Events
// ============================================================

public abstract record AxiomEvent
{
    public string SessionId { get; init; } = "";
    public string Type => GetType().Name;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public record ProgressEvent : AxiomEvent
{
    public string Phase { get; init; } = "";
    public string? Message { get; init; }
    public int ProgressPercent { get; init; }

    // Logical worker id (e.g. "coordinator", "worker-0"...), for UI grouping
    public string? WorkerId { get; init; }

    // DSL step meta (for debugging / UI)
    public int? Depth { get; init; }
    public string? StepId { get; init; }
    public string? StepType { get; init; }
    public string? StepStatus { get; init; }

    // Readable content (best-effort)
    public string? SystemPrompt { get; init; }
    public string? UserPrompt { get; init; }
    public string? AssistantResponsePreview { get; init; }
    public string? AssistantResponse { get; init; }

    // Failure details (when stepStatus == Failed)
    public string? Error { get; init; }

    // Streaming meta (PaperReview-like)
    public string? ProviderName { get; init; }
    public int? TokenIndex { get; init; }
    public string? TokenDelta { get; init; }

    // vote
    public int VoteRound { get; init; }
    public int VoteMaxRounds { get; init; }
    public int VoteK { get; init; }
    public int VoteCurrentVotes { get; init; }

    // parallel
    public int ParallelTotal { get; init; }
    public int ParallelCompleted { get; init; }
    public int ParallelFailed { get; init; }

    // totals
    public int TotalLlmCalls { get; init; }
    public long TotalTokens { get; init; }
}

public record ResultEvent : AxiomEvent
{
    public bool Success { get; init; }
    public string? Content { get; init; }
    public string? Error { get; init; }
    public int TotalLlmCalls { get; init; }
    public long TotalTokens { get; init; }
}

public record ErrorEvent : AxiomEvent
{
    public string Message { get; init; } = "";
    public string? StackTrace { get; init; }
}

// ============================================================
//  Graph (Axiom/Theorem Dependencies)
// ============================================================

public record TheoremNode
{
    public string Id { get; init; } = "";
    public string Statement { get; init; } = "";
    public List<string> DependsOn { get; init; } = [];
}

public record GraphEvent : AxiomEvent
{
    public int Iteration { get; init; }
    public List<string> Axioms { get; init; } = [];
    public List<TheoremNode> Theorems { get; init; } = [];
}


