using System.Text.Json;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.WithTool.Abstractions;

namespace MemoryDemo;

/// <summary>
/// Memory demo agent:
/// - State.History replay (EnableChatHistoryInState)
/// - Compaction (sliding window + history_summary)
/// - Tool-based recall via built-in 'search_memory'
/// </summary>
public sealed class MemoryDemoAgent : AIGAgentBase
{
    public MemoryDemoAgent()
    {
        // Make the demo deterministic and easy to trigger.
        EnableChatHistoryInState = true;
        EnableChatHistoryCompaction = true;
        ChatHistoryMaxMessages = 8;
        ChatHistorySummaryMaxChars = 1200;

        SystemPrompt =
            """
            You are a helpful assistant in a memory demo.

            Rules:
            - Treat "Conversation summary (memory)" as authoritative compressed memory.
            - If the user asks about earlier details, call tool 'search_memory' before answering.
            - Be concise and factual. Do not invent.
            """;
    }

    public override Task<string> GetDescriptionAsync() =>
        Task.FromResult("MemoryDemoAgent (History + Compaction + CQRS + search_memory)");

    // ============================================================
    //  Demo-only: force CQRS projection after each chat
    //
    //  WHY:
    //  - In real systems, CQRS projection happens after state persistence (OnStateChangedAsync).
    //  - This demo keeps EventStore optional; we still want to show projected read-model.
    // ============================================================
    public override async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var resp = await base.ChatAsync(request, cancellationToken);

        // Best-effort projection: do not break chat if CQRS isn't configured.
        await ProjectStateAsync(GetState(), cancellationToken);

        return resp;
    }

    // ============================================================
    //  Demo-only: seed memory without calling LLM
    //
    //  WHY:
    //  - Helps validate search_memory + CQRS pipeline even without external LLM connectivity.
    // ============================================================
    public async Task SeedAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        // Put something into short-term history (Layer 1)
        AddMessageToHistory(text.Trim(), AevatarChatRole.User);
        AddMessageToHistory("(seeded) ok, I remember it.", AevatarChatRole.Assistant);

        // Put something into rolling summary (Layer 2) for easier searching
        var summary = GetHistorySummary() ?? string.Empty;
        var next = string.IsNullOrWhiteSpace(summary)
            ? $"[seed] {text}".Trim()
            : (summary + "\n" + $"[seed] {text}").Trim();

        GetState().Context["history_summary"] = next;

        // Project to CQRS read-model (demo)
        await ProjectStateAsync(GetState(), ct);
    }

    public string? GetHistorySummary()
    {
        var state = GetState();
        return state.Context != null && state.Context.TryGetValue("history_summary", out var s) ? s : null;
    }

    public async Task<ToolExecutionResult> SearchMemoryToolAsync(
        string query,
        int maxResults = 10,
        string memoryType = "all",
        CancellationToken ct = default)
    {
        await InitializeToolsAsync(ct);

        var parameters = new Dictionary<string, object>
        {
            ["query"] = query,
            ["maxResults"] = maxResults,
            ["memoryType"] = memoryType
        };

        var execCtx = new ToolExecutionContext
        {
            AgentId = Id.ToString(),
            ToolManager = ToolManager,
            PublishEventCallback = msg => PublishAsync(msg, ct: ct),
            Logger = Logger,
            GetSessionId = () => Id.ToString()
        };

        return await ToolManager.ExecuteToolAsync("search_memory", parameters, execCtx, ct);
    }
}


