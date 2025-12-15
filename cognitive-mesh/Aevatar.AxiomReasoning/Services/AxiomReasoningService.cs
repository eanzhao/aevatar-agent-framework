using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Aevatar.AxiomReasoning.Models;
using Aevatar.CognitiveMesh.Abstractions;
using Aevatar.CognitiveMesh.Strategies;
using Microsoft.Extensions.Logging;
using ErrorEvent = Aevatar.AxiomReasoning.Models.ErrorEvent;
using ResultEvent = Aevatar.AxiomReasoning.Models.ResultEvent;

namespace Aevatar.AxiomReasoning.Services;

// ============================================================
//  AXIOM REASONING SERVICE
//  职责：会话管理 + 启动 CognitiveStrategy(axiom_reasoning.yaml) + SSE
// ============================================================

public sealed class AxiomReasoningService
{
    private readonly ConcurrentDictionary<string, AxiomSession> _sessions = new();
    private readonly CognitiveStrategy _cognitiveStrategy;
    private readonly AxiomReasoningEventBridge _eventBridge;
    private readonly SupabaseService _supabaseService;
    private readonly ILogger<AxiomReasoningService> _logger;
    private readonly string _outputBasePath;

    public AxiomReasoningService(
        CognitiveStrategy cognitiveStrategy,
        AxiomReasoningEventBridge eventBridge,
        SupabaseService supabaseService,
        ILoggerFactory loggerFactory)
    {
        _cognitiveStrategy = cognitiveStrategy;
        _eventBridge = eventBridge;
        _supabaseService = supabaseService;
        _logger = loggerFactory.CreateLogger<AxiomReasoningService>();

        _outputBasePath = Path.Combine(Directory.GetCurrentDirectory(), "output");
        Directory.CreateDirectory(_outputBasePath);
    }

    // ─────────────────────────────────────────────────────────
    //  会话管理
    // ─────────────────────────────────────────────────────────

    public IEnumerable<object> GetSessions() =>
        _sessions.Values
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new
            {
                s.Id,
                status = s.Status.ToString(),
                s.CreatedAt,
                s.ProgressPercent,
                s.CurrentPhase,
                s.TotalLlmCalls,
                s.TotalTokens,
                workflow = s.Workflow,
                language = s.Language,
                s.K,
                s.MaxRounds,
                maxDepth = s.MaxDepth
            });

    private sealed record CreateSessionRequest
    {
        public string? Axioms { get; init; }
        public string? Goal { get; init; }
        public string? Workflow { get; init; }
        public string? Language { get; init; }
        public int? K { get; init; }
        public int? MaxRounds { get; init; }
        public int? MaxDepth { get; init; }
        public string? ProviderName { get; init; }

        // Long-run budgets (optional)
        public int? MaxDurationMinutes { get; init; }
        public int? MaxLlmCalls { get; init; }
        public long? MaxTokens { get; init; }

        // Workflow behavior
        public bool? ContinueOnFailure { get; init; }
    }

    public async Task<object> CreateSessionAsync(string configJson)
    {
        try
        {
            var req = JsonSerializer.Deserialize<CreateSessionRequest>(configJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new ArgumentException("Invalid request");

            var axiomsText = (req.Axioms ?? "").Trim();
            var goal = (req.Goal ?? "").Trim();

            if (string.IsNullOrWhiteSpace(axiomsText))
                return new { success = false, error = "axioms is required" };
            // goal is optional (used as "focus" / direction hint for theorem discovery)

            var session = new AxiomSession
            {
                AxiomsText = axiomsText,
                Goal = goal,
                Workflow = ResolveWorkflow(req.Workflow),
                Language = NormalizeLanguage(req.Language),
                K = req.K is > 0 ? req.K.Value : 3,
                MaxRounds = req.MaxRounds is > 0 ? req.MaxRounds.Value : 10,
                MaxDepth = req.MaxDepth is > 0 ? req.MaxDepth.Value : 10,

                // Budgets (keep prior defaults if not provided)
                MaxDurationMinutes = req.MaxDurationMinutes is > 0 ? Math.Clamp(req.MaxDurationMinutes.Value, 1, 24 * 60) : 30,
                MaxLlmCallsBudget = req.MaxLlmCalls is > 0 ? Math.Clamp(req.MaxLlmCalls.Value, 1, 200_000) : 300,
                MaxTokensBudget = req.MaxTokens is > 0 ? Math.Clamp(req.MaxTokens.Value, 1, 200_000_000) : 800_000,

                ContinueOnFailure = req.ContinueOnFailure ?? false
            };

            _sessions[session.Id] = session;
            _logger.LogInformation("Created axiom session: {Id}", session.Id);

            // 立即发一个初始事件，方便前端接入
            session.EventChannel.Writer.TryWrite(new Aevatar.AxiomReasoning.Models.ProgressEvent
            {
                SessionId = session.Id,
                Phase = "CREATED",
                Message = "Session created",
                ProgressPercent = 0
            });

            // ProviderName 作为“运行参数”不放进 session（避免泄漏/混淆），运行时从请求读取或走默认
            await Task.CompletedTask;

            return new
            {
                success = true,
                sessionId = session.Id
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create session");
            return new { success = false, error = ex.Message };
        }
    }

    private string ResolveWorkflow(string? requested)
    {
        var wf = string.IsNullOrWhiteSpace(requested) ? "axiom_theorem_loop" : requested.Trim();

        try
        {
            var available = _cognitiveStrategy.GetAvailableWorkflows();
            if (available.Any(x => string.Equals(x, wf, StringComparison.OrdinalIgnoreCase)))
                return wf;

            _logger.LogWarning("Unknown workflow '{Workflow}', falling back to axiom_theorem_loop. Available=[{List}]",
                wf, string.Join(", ", available));
        }
        catch (Exception ex)
        {
            // best-effort: if registry cannot be read, still allow default workflow
            _logger.LogWarning(ex, "Failed to list workflows; using default workflow");
        }

        return "axiom_theorem_loop";
    }

    private static string NormalizeLanguage(string? requested)
    {
        var raw = (requested ?? "").Trim();
        if (string.IsNullOrWhiteSpace(raw)) return "English";

        // Common aliases
        var lower = raw.ToLowerInvariant();
        if (lower is "zh" or "zh-cn" or "zh-hans" or "cn" or "chinese" or "中文" or "汉语")
            return "Chinese";
        if (lower is "en" or "en-us" or "en-gb" or "english" or "英文")
            return "English";

        // If user already passes a natural language label (e.g. "Japanese"), keep it.
        return raw;
    }

    // ─────────────────────────────────────────────────────────
    //  执行
    // ─────────────────────────────────────────────────────────

    public async Task<object> StartAsync(string sessionId, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return new { success = false, error = "Session not found" };

        if (session.Status == AxiomSessionStatus.Running)
            return new { success = false, error = "Already running" };

        session.Status = AxiomSessionStatus.Running;
        session.OutputDir = Path.Combine(_outputBasePath, session.Id);
        Directory.CreateDirectory(session.OutputDir);

        _ = ExecuteAsync(session, session.CancellationTokenSource.Token);
        await Task.CompletedTask;

        return new { success = true, sessionId };
    }

    public object Stop(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return new { success = false, error = "Session not found" };

        if (session.Status != AxiomSessionStatus.Running)
            return new { success = false, error = "Not running" };

        try
        {
            session.CancellationTokenSource.Cancel();
            session.Status = AxiomSessionStatus.Cancelled;
            session.Error = "Stopped by user";

            session.EventChannel.Writer.TryWrite(new ErrorEvent
            {
                SessionId = session.Id,
                Message = "Stopped by user"
            });

            session.EventChannel.Writer.TryComplete();
            return new { success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop session {Id}", sessionId);
            return new { success = false, error = ex.Message };
        }
    }

    private async Task ExecuteAsync(AxiomSession session, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var task = BuildTask(session);
            var options = BuildReasoningOptions(session);
            var progress = new Progress<ReasoningProgress>(p => _eventBridge.HandleProgress(session, p));

            _logger.LogInformation("[{Id}] Starting workflow={Workflow}, K={K}, N={N}, max_depth={Depth}",
                session.Id, options.CognitiveWorkflow, options.CognitiveConsensusK, options.CognitiveWorkerCount, options.CognitiveMaxDepth);

            var result = await _cognitiveStrategy.ExecuteAsync(task, options, progress, ct);

            sw.Stop();
            session.Duration = sw.Elapsed;
            session.Result = result;
            session.TotalLlmCalls = result.TotalLlmCalls;
            session.TotalTokens = result.TotalTokens;

            session.Status = result.Success ? AxiomSessionStatus.Completed : AxiomSessionStatus.Failed;
            session.Error = result.Error;

            // 保存 artifacts（best-effort）
            string? stateJson = null;
            string? theoremsJson = null;
            try
            {
                SaveFile(session, "artifacts", "result.txt", result.Content ?? "");

                // axiom_theorem_loop.yaml outputs `state` (object) + `theorems` (list)
                stateJson = ExtractField(result.Content, "state") ?? "{}";
                theoremsJson = ExtractField(result.Content, "theorems") ?? "[]";
                SaveFile(session, "artifacts", "state.json", stateJson);
                SaveFile(session, "artifacts", "theorems.json", theoremsJson);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[{Id}] Failed to save artifacts (ignored)", session.Id);
            }

            // Supabase persistence (best-effort)
            try
            {
                if (_supabaseService.IsEnabled)
                {
                    await _supabaseService.SaveResultAsync(
                        sessionId: session.Id,
                        axiomsText: session.AxiomsText,
                        goal: session.Goal,
                        status: session.Status.ToString(),
                        stateJson: stateJson,
                        theoremsJson: theoremsJson,
                        content: result.Content,
                        error: result.Error,
                        llmCalls: result.TotalLlmCalls,
                        totalTokens: result.TotalTokens,
                        durationSeconds: session.Duration.TotalSeconds);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[{Id}] Failed to persist result to Supabase (ignored)", session.Id);
            }

            session.EventChannel.Writer.TryWrite(new ResultEvent
            {
                SessionId = session.Id,
                Success = result.Success,
                Content = result.Content,
                Error = result.Error,
                TotalLlmCalls = result.TotalLlmCalls,
                TotalTokens = result.TotalTokens
            });

            if (!result.Success)
            {
                _logger.LogError("[{Id}] Workflow failed: {Error}", session.Id, result.Error ?? "(unknown)");
                session.EventChannel.Writer.TryWrite(new ErrorEvent
                {
                    SessionId = session.Id,
                    Message = result.Error ?? "Execution failed"
                });
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            session.Duration = sw.Elapsed;
            session.Status = AxiomSessionStatus.Failed;
            session.Error = ex.Message;

            _logger.LogError(ex, "[{Id}] Execution failed", session.Id);
            session.EventChannel.Writer.TryWrite(new ErrorEvent
            {
                SessionId = session.Id,
                Message = ex.Message,
                StackTrace = ex.StackTrace
            });
        }
        finally
        {
            session.EventChannel.Writer.TryComplete();
        }
    }

    private static string BuildTask(AxiomSession session)
    {
        // NOTE:
        // - CognitiveStrategy 只会注入 `task`/`context` 变量给 workflow。
        // - theorem-loop workflow 会从 raw_task 中提取 axioms + optional focus。
        var focus = string.IsNullOrWhiteSpace(session.Goal) ? "" : session.Goal.Trim();

        return $"""
               AXIOM THEOREM LOOP

               AXIOMS (one per line, authoritative):
               {session.AxiomsText}

               Focus (optional):
               {focus}

               ContinueOnFailure:
               {session.ContinueOnFailure}
               """;
    }

    private ReasoningOptions BuildReasoningOptions(AxiomSession session)
    {
        var k = Math.Clamp(session.K, 1, 9);
        var n = Math.Clamp(2 * k - 1, 1, 15);

        return new ReasoningOptions
        {
            ProviderName = Aevatar.Agents.Abstractions.AevatarAgentsConstants.DefaultProviderName,
            MaxLlmCalls = session.MaxLlmCallsBudget,
            MaxTokens = session.MaxTokensBudget,
            MaxDuration = TimeSpan.FromMinutes(Math.Clamp(session.MaxDurationMinutes, 1, 24 * 60)),
            Context = new Dictionary<string, string>
            {
                // Propagate workflow behavior flags to CognitiveStrategy initial variables
                ["continue_on_failure"] = session.ContinueOnFailure ? "true" : "false",
                ["language"] = session.Language
            },

            // Theorem discovery loop: coordinator proposes -> workers prove -> vote judge -> iterate
            CognitiveWorkflow = session.Workflow,
            CognitiveWorkerCount = n,
            CognitiveConsensusK = k,
            CognitiveMaxRounds = Math.Clamp(session.MaxRounds, 1, 50),
            CognitiveMaxDepth = Math.Clamp(session.MaxDepth, 1, 200),
            CognitiveSemanticSimilarity = 0.85f,
            CognitiveTimeoutMinutes = Math.Clamp(session.MaxDurationMinutes, 1, 24 * 60)
        };
    }

    // ─────────────────────────────────────────────────────────
    //  查询
    // ─────────────────────────────────────────────────────────

    public object GetStatus(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return new { status = "not_found" };

        return new
        {
            status = session.Status.ToString().ToLowerInvariant(),
            sessionId = session.Id,
            session.ProgressPercent,
            phase = session.CurrentPhase,
            session.TotalLlmCalls,
            session.TotalTokens,
            duration = session.Duration.TotalSeconds
        };
    }

    public object GetResult(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return new { success = false, error = "Session not found" };

        return new
        {
            success = session.Status == AxiomSessionStatus.Completed,
            content = session.Result?.Content,
            error = session.Error,
            session.TotalLlmCalls,
            session.TotalTokens,
            duration = session.Duration.TotalSeconds
        };
    }

    public IEnumerable<object> GetArtifacts(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return [];

        if (!session.Files.TryGetValue("artifacts", out var files))
            return [];

        return files.Keys.OrderBy(k => k).Select(name => new { category = "artifacts", name });
    }

    public bool TryGetFileContent(string sessionId, string category, string fileName, out string content)
    {
        content = "";
        if (!_sessions.TryGetValue(sessionId, out var session)) return false;
        if (!session.Files.TryGetValue(category, out var files)) return false;
        if (!files.TryGetValue(fileName, out var c)) return false;
        content = c;
        return true;
    }

    public async IAsyncEnumerable<AxiomEvent> GetEventStreamAsync(
        string sessionId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            yield break;

        await foreach (var evt in session.EventChannel.Reader.ReadAllAsync(ct))
        {
            yield return evt;
        }
    }

    private void SaveFile(AxiomSession session, string category, string fileName, string content)
    {
        session.Files.GetOrAdd(category, _ => new ConcurrentDictionary<string, string>())[fileName] = content;

        if (string.IsNullOrEmpty(session.OutputDir)) return;

        try
        {
            var dir = Path.Combine(session.OutputDir, category);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, fileName), content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Id}] Failed to write file {File}", session.Id, fileName);
        }
    }

    // ============================================================
    //  解析 output 字段（best-effort）
    //
    //  CognitiveStrategy 会把 workflow 输出序列化为“字符串”，其格式可能是：
    //  - JSON string（如果 output 是字典，可能被 SerializeOutput 转为 JSON）
    //  - plain text
    //
    //  这里做最小可用：
    //  1) 若 content 是 JSON object，尝试提取字段
    //  2) 否则返回 null
    // ============================================================
    private static string? ExtractField(string? content, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty(fieldName, out var value)) return null;
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                _ => value.GetRawText()
            };
        }
        catch
        {
            return null;
        }
    }
}


