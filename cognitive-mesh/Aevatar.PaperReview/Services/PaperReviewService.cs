using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Maker;
using Aevatar.PaperReview.Models;

namespace Aevatar.PaperReview.Services;

// ============================================================
//  PAPER REVIEW SERVICE
//  论文评审核心服务 - 基于 MAKER 策略的多专家共识系统
// ============================================================

/// <summary>
/// 论文评审服务。
/// 使用 MAKER 系统进行多专家协同评审，达成共识后输出综合评审意见。
/// </summary>
public sealed class PaperReviewService
{
    private readonly IMakerExecutor _makerExecutor;
    private readonly SupabaseService _supabase;
    private readonly ILogger<PaperReviewService> _logger;
    private readonly ConcurrentDictionary<string, ReviewSession> _sessions = new();
    private readonly ConcurrentDictionary<string, StageTracker> _stageTrackers = new();
    private readonly string _uploadsBasePath;
    private readonly string _outputBasePath;
    
    /// <summary>阶段跟踪器 - 记录阶段开始时间和统计。</summary>
    private sealed class StageTracker
    {
        public string CurrentStage { get; set; } = "";
        public DateTimeOffset StageStartTime { get; set; }
        public int LlmCalls { get; set; }
        public long Tokens { get; set; }
        public int VotingRounds { get; set; }
        public int WorkerCount { get; set; }
        public bool ConsensusReached { get; set; }
        
        // 详情收集
        public List<CandidateDetail> Candidates { get; } = [];
        public List<WorkerOutput> WorkerOutputs { get; } = [];
        public string? WinnerContent { get; set; }
        
        public void Reset()
        {
            LlmCalls = 0;
            Tokens = 0;
            VotingRounds = 0;
            WorkerCount = 0;
            ConsensusReached = false;
            Candidates.Clear();
            WorkerOutputs.Clear();
            WinnerContent = null;
        }
    }

    public PaperReviewService(
        IMakerExecutor makerExecutor,
        SupabaseService supabase,
        ILogger<PaperReviewService> logger)
    {
        _makerExecutor = makerExecutor;
        _supabase = supabase;
        _logger = logger;
        _uploadsBasePath = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
        _outputBasePath = Path.Combine(Directory.GetCurrentDirectory(), "output");
        Directory.CreateDirectory(_uploadsBasePath);
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
                s.Title,
                s.Authors,
                type = s.Type.ToString(),
                s.VenueType,
                status = s.Status.ToString(),
                s.CreatedAt,
                s.ProgressPercent,
                s.TotalLlmCalls,
                s.TotalTokens
            });

    public async Task<object> CreateSessionAsync(string configJson)
    {
        try
        {
            var request = JsonSerializer.Deserialize<CreateSessionRequest>(configJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new ArgumentException("Invalid request");

            var session = new ReviewSession
            {
                Title = request.Title,    // may be null/empty
                Authors = request.Authors, // may be null/empty
                Type = Enum.TryParse<ReviewType>(request.ReviewType, true, out var rt) ? rt : ReviewType.Standard,
                VenueType = request.VenueType ?? "AI Conference",
                UploadId = request.UploadId
            };

            // 优先级：1. 直接提供的内容 2. 从老 session 复制 3. 从上传文件加载
            if (!string.IsNullOrEmpty(request.PaperContent))
            {
                session.PaperContent = request.PaperContent;
            }
            else if (!string.IsNullOrEmpty(request.CopyFromSessionId) 
                     && _sessions.TryGetValue(request.CopyFromSessionId, out var oldSession))
            {
                // 从老 session 复制论文内容
                session.PaperContent = oldSession.PaperContent;
                session.UploadId = oldSession.UploadId;
                _logger.LogInformation("Copied paper content from session {OldId} to {NewId}", 
                    request.CopyFromSessionId, session.Id);
            }
            else if (!string.IsNullOrEmpty(request.UploadId))
            {
                (session.PaperContent, var fileName) = await LoadPaperFromUploadAsync(request.UploadId);
                // 如果没填标题，用文件名
                if (string.IsNullOrWhiteSpace(session.Title))
                    session.Title = string.IsNullOrWhiteSpace(fileName) ? "Untitled" : fileName;
                // 作者留空则标记 Unknown
                if (string.IsNullOrWhiteSpace(session.Authors))
                    session.Authors = "Unknown";
            }

            // 如果仍无标题，给默认
            if (string.IsNullOrWhiteSpace(session.Title))
                session.Title = "Untitled";
            if (string.IsNullOrWhiteSpace(session.Authors))
                session.Authors = "Unknown";

            _sessions[session.Id] = session;

            _logger.LogInformation("Created review session: {SessionId} - {Title}", session.Id, session.Title);

            return new
            {
                success = true,
                sessionId = session.Id,
                session.Title,
                type = session.Type.ToString(),
                hasPaper = !string.IsNullOrEmpty(session.PaperContent)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create session");
            return new { success = false, error = ex.Message };
        }
    }

    public async Task<object> UploadPaperAsync(IFormFileCollection files, CancellationToken ct)
    {
        try
        {
            if (files.Count == 0)
                return new { success = false, error = "No file uploaded" };

            var file = files[0];
            var uploadId = Guid.NewGuid().ToString("N")[..12];
            var uploadDir = Path.Combine(_uploadsBasePath, uploadId);
            Directory.CreateDirectory(uploadDir);

            var fileName = Path.GetFileName(file.FileName);
            var filePath = Path.Combine(uploadDir, fileName);

            await using var stream = new FileStream(filePath, FileMode.Create);
            await file.CopyToAsync(stream, ct);

            _logger.LogInformation("Uploaded paper: {FileName} ({Size} bytes)", fileName, file.Length);

            return new
            {
                success = true,
                uploadId,
                fileName,
                size = file.Length
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload paper");
            return new { success = false, error = ex.Message };
        }
    }

    private async Task<(string Content, string FileName)> LoadPaperFromUploadAsync(string uploadId)
    {
        var uploadDir = Path.Combine(_uploadsBasePath, uploadId);
        if (!Directory.Exists(uploadDir))
            return ("", "");

        var files = Directory.GetFiles(uploadDir);
        if (files.Length == 0)
            return ("", "");

        var filePath = files[0];
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var fileName = Path.GetFileName(filePath);

        if (ext == ".pdf")
        {
            try
            {
                return (ExtractPdfText(filePath), fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse PDF: {File}", filePath);
                return ("", fileName);
            }
        }

        var content = await File.ReadAllTextAsync(filePath);
        return (content, fileName);
    }

    // Extremely lightweight PDF text extraction (best-effort, no external deps)
    private static string ExtractPdfText(string filePath)
    {
        // Primary: PdfPig extraction (preserves layout better than naive regex)
        try
        {
            var sb = new StringBuilder();
            using var doc = PdfDocument.Open(filePath);
            foreach (Page page in doc.GetPages())
            {
                sb.AppendLine(page.Text);
                sb.AppendLine();
            }
            var text = sb.ToString();
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }
        catch (Exception ex)
        {
            // Fallback to naive parsing below
            // We log at debug to avoid noisy errors on tricky PDFs
            System.Diagnostics.Debug.WriteLine($"PdfPig failed, fallback to regex: {ex.Message}");
        }

        // Fallback: extremely lightweight regex-based extraction (best-effort)
        var bytes = File.ReadAllBytes(filePath);
        var raw = Encoding.Latin1.GetString(bytes);

        var matches = Regex.Matches(raw, @"\(([^\\)]*(?:\\.[^\\)]*)*)\)");
        var fallback = new StringBuilder();

        foreach (Match m in matches.Cast<Match>())
        {
            var text = m.Groups[1].Value;
            text = text.Replace("\\(", "(")
                       .Replace("\\)", ")")
                       .Replace("\\n", "\n")
                       .Replace("\\r", "\r")
                       .Replace("\\t", "\t")
                       .Replace("\\\\", "\\");
            fallback.Append(text);
            fallback.Append(' ');
        }

        return fallback.ToString();
    }

    // ─────────────────────────────────────────────────────────
    //  评审执行
    // ─────────────────────────────────────────────────────────

    public async Task<object> StartReviewAsync(string sessionId, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return new { success = false, error = "Session not found" };

        if (session.Status == ReviewStatus.Reviewing)
            return new { success = false, error = "Already reviewing" };

        if (string.IsNullOrEmpty(session.PaperContent))
            return new { success = false, error = "No paper content" };

        session.Status = ReviewStatus.Reviewing;
        session.OutputDir = Path.Combine(_outputBasePath, session.Id);
        Directory.CreateDirectory(session.OutputDir);

        _logger.LogInformation("Starting review for session {SessionId}: {Title}", sessionId, session.Title);

        _ = ExecuteReviewAsync(session, session.CancellationTokenSource.Token);

        return new { success = true, sessionId };
    }

    public object StopReview(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return new { success = false, error = "Session not found" };

        if (session.Status != ReviewStatus.Reviewing)
            return new { success = false, error = "Not reviewing" };

        try
        {
            session.CancellationTokenSource.Cancel();
            session.Status = ReviewStatus.Cancelled;
            session.Error = "Stopped by user";

            session.EventChannel.Writer.TryWrite(new Models.ErrorEvent
            {
                SessionId = sessionId,
                Message = "Review stopped by user"
            });
            session.EventChannel.Writer.TryComplete();

            _logger.LogInformation("Stopped review for session {SessionId}", sessionId);
            return new { success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop review {SessionId}", sessionId);
            return new { success = false, error = ex.Message };
        }
    }

    private async Task ExecuteReviewAsync(ReviewSession session, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        // 保存初始记录到 Supabase
        await _supabase.SaveReviewAsync(
            session.Id,
            session.Title,
            session.Authors,
            session.Type.ToString(),
            session.VenueType,
            "Reviewing",
            null, null,
            0, 0, 0);

        try
        {
            var reviewTask = BuildReviewTask(session);
            var options = BuildMakerOptions(session);

            var result = await _makerExecutor.ExecuteAsync(reviewTask, options, ct);

            stopwatch.Stop();
            session.Status = result.Success ? ReviewStatus.Completed : ReviewStatus.Failed;
            session.Duration = stopwatch.Elapsed;
            session.TotalLlmCalls = result.TotalLLMCalls;
            session.TotalTokens = result.TotalTokens;

            _logger.LogInformation("[{Session}] Review completed. Success={Success}, Duration={Duration}s",
                session.Id, result.Success, stopwatch.Elapsed.TotalSeconds);

            string? reportUrl = null;
            if (!string.IsNullOrEmpty(result.Content))
            {
                var report = FormatReviewReport(session, result);
                SaveFile(session, "reports", "review_report.md", report);
                
                // 上传报告到 Supabase Storage
                reportUrl = await _supabase.UploadReportAsync(session.Id, report);
            }

            // 更新 Supabase 记录
            await _supabase.UpdateReviewAsync(
                session.Id,
                session.Status.ToString(),
                result.Content,
                result.Error,
                result.TotalLLMCalls,
                result.TotalTokens,
                session.Duration.TotalSeconds,
                reportUrl);

            SendEvent(session, new Models.ResultEvent
            {
                SessionId = session.Id,
                Success = result.Success,
                Content = result.Content,
                Error = result.Error,
                TotalLlmCalls = result.TotalLLMCalls,
                TotalTokens = result.TotalTokens
            });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            session.Status = ReviewStatus.Failed;
            session.Duration = stopwatch.Elapsed;
            session.Error = ex.Message;

            _logger.LogError(ex, "[{Session}] Review failed", session.Id);

            // 更新失败状态到 Supabase
            await _supabase.UpdateReviewAsync(
                session.Id,
                "Failed",
                null,
                ex.Message,
                session.TotalLlmCalls,
                session.TotalTokens,
                session.Duration.TotalSeconds);

            SendEvent(session, new Models.ErrorEvent
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

    private string BuildReviewTask(ReviewSession session)
    {
        // ─────────────────────────────────────────────────────────
        //  Two-Phase Prompt Architecture
        //  Phase 1: Paper analysis & dimension extraction (built into decomposer)
        //  Phase 2: Per-dimension deep review (solver handles each)
        // ─────────────────────────────────────────────────────────
        
        var (dimCount, scrutinyLevel, focusAreas) = session.Type switch
        {
            ReviewType.Quick => (2, "high-level", "core contribution and major flaws"),
            ReviewType.Standard => (4, "standard", "technical soundness, novelty, experiments, presentation"),
            ReviewType.Detailed => (5, "detailed", "methodology, innovation, evaluation, clarity, related work"),
            ReviewType.Rigorous => (6, "rigorous", "all technical aspects with deep scrutiny"),
            ReviewType.Critical => (7, "exhaustive", "every aspect with maximum rigor and skepticism"),
            _ => (4, "standard", "key aspects")
        };

        return $$"""
            You are a Senior Editor and Expert Reviewer for a top-tier {{session.VenueType}}.
            
            ═══════════════════════════════════════════════════════════════
            REVIEW METHODOLOGY: Multi-Dimensional Analysis
            ═══════════════════════════════════════════════════════════════
            
            You will conduct a {{scrutinyLevel}} review by analyzing the paper across {{dimCount}} dimensions.
            Focus areas: {{focusAreas}}
            
            ═══════════════════════════════════════════════════════════════
            PAPER METADATA
            ═══════════════════════════════════════════════════════════════
            
            Title: {{session.Title}}
            Authors: {{session.Authors}}
            Venue Type: {{session.VenueType}}
            
            ═══════════════════════════════════════════════════════════════
            REVIEW STRUCTURE
            ═══════════════════════════════════════════════════════════════
            
            For each dimension, provide:
            
            ### [Dimension Name]
            
            **Assessment**: 200-400 words of detailed evaluation
            - Cite specific sections, equations, figures, tables by reference
            - Explain WHY each point matters for the paper's contribution
            - Distinguish critical issues from minor suggestions
            
            **Strengths**:
            - [Strength with specific evidence from paper]
            - ...
            
            **Weaknesses**:
            - [Weakness with specific evidence + concrete suggestion]
            - ...
            
            **Score**: X/5 (1=Major issues, 5=Excellent)
            
            ---
            
            After all dimensions, conclude with:
            
            ## Summary
            2-3 sentences capturing the core contribution and overall assessment.
            
            ## Questions for Authors
            3-5 clarifying questions that would help address concerns.
            
            ## Overall Recommendation
            Strong Accept / Accept / Weak Accept / Borderline / Weak Reject / Reject / Strong Reject
            
            Justification: Why this recommendation, considering all dimensions.
            
            ## Confidence: X/5
            How confident are you in this review?
            
            ═══════════════════════════════════════════════════════════════
            PAPER CONTENT
            ═══════════════════════════════════════════════════════════════
            
            {{session.PaperContent}}
            
            ═══════════════════════════════════════════════════════════════
            END OF PAPER
            ═══════════════════════════════════════════════════════════════
            """;
    }

    private MakerOptions BuildMakerOptions(ReviewSession session)
    {
        // ─────────────────────────────────────────────────────────
        //  MAKER 参数映射 (基于论文公式)
        //  K = 领先票数, N = 2K - 1 = 采样数
        // ─────────────────────────────────────────────────────────
        var (k, n, desc) = MakerParameters.GetParams(session.Type);
        
        var reliability = session.Type switch
        {
            ReviewType.Quick    => ReliabilityLevel.Low,       // K=1
            ReviewType.Standard => ReliabilityLevel.Medium,    // K=2
            ReviewType.Detailed => ReliabilityLevel.High,      // K=3
            ReviewType.Rigorous => ReliabilityLevel.VeryHigh,  // K=4
            ReviewType.Critical => ReliabilityLevel.Critical,  // K=5
            _ => ReliabilityLevel.Medium
        };
        
        _logger.LogInformation(
            "[{Session}] MAKER Config: {Type} → K={K}, N={N} ({Desc})",
            session.Id, session.Type, k, n, desc);

        // 发送配置事件
        SendEvent(session, new StageLogEvent
        {
            SessionId = session.Id,
            Stage = "Configuration",
            Status = "completed",
            Summary = $"MAKER initialized: K={k}, N={n} workers ({desc})",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow,
            Stats = new StageStats { WorkerCount = n }
        });

        // ─────────────────────────────────────────────────────────
        //  Two-Phase Strategies
        //  Phase 1: Decomposer analyzes paper → generates dimensions
        //  Phase 2: Solver reviews each dimension independently
        //  Phase 3: Composer assembles final review
        // ─────────────────────────────────────────────────────────
        var decomposer = new Strategies.PaperReviewDecomposer(
            session.VenueType,
            session.Type.ToString(),
            session.PaperContent);
        
        var solver = new Strategies.PaperReviewSolver(
            session.PaperContent,
            session.Title,
            session.VenueType);
        
        var composer = new Strategies.PaperReviewComposer(
            session.Title,
            session.VenueType,
            session.Type.ToString());
        
        _logger.LogInformation(
            "[{Session}] Two-phase review: Decomposer will analyze paper and generate review dimensions",
            session.Id);

        // ─────────────────────────────────────────────────────────
        //  LLM 调用预算估算
        //  - 分解阶段：N workers 投票 = n 调用
        //  - 每个维度：N workers 投票 = n 调用 × 维度数（4-7个）
        //  - 可能的多轮投票：额外 2-3 倍
        //  - 整合阶段：1-2 调用
        //  
        //  保守估计：n * (1 + 维度数 * 3) + 10
        //  例如 n=3, 5个维度：3 * (1 + 5*3) + 10 = 58 调用
        //  为安全起见，设置为 200 * n
        // ─────────────────────────────────────────────────────────
        var estimatedDimensions = session.Type switch
        {
            ReviewType.Quick => 3,
            ReviewType.Standard => 5,
            ReviewType.Detailed => 6,
            ReviewType.Rigorous => 7,
            ReviewType.Critical => 8,
            _ => 5
        };
        
        // 充足的预算：每个维度可能需要多轮投票
        var llmCallBudget = n * (1 + estimatedDimensions * 4) + 20;
        var tokenBudget = 200_000 * n;  // 每个 worker 调用约 30-50K tokens
        
        _logger.LogInformation(
            "[{Session}] LLM Budget: {Calls} calls, {Tokens} tokens for {Dims} dimensions",
            session.Id, llmCallBudget, tokenBudget, estimatedDimensions);

        return new MakerOptions
        {
            ProviderName = AevatarAgentsConstants.DefaultProviderName,
            Reliability = reliability,
            CustomK = k,
            MaxTotalLlmCalls = llmCallBudget,
            MaxTotalTokens = tokenBudget,
            MaxDuration = TimeSpan.FromMinutes(15 + estimatedDimensions * 3),
            
            // ─────────────────────────────────────────────────────────
            //  CRITICAL: Use Academic mode to force decomposition!
            //  Production mode skips decomposition if IsAtomic returns true
            // ─────────────────────────────────────────────────────────
            Mode = ExecutionMode.Academic,
            Granularity = DecompositionGranularity.Balanced,
            
            // Inject custom strategies
            Decomposer = decomposer,
            Solver = solver,
            Composer = composer,
            
            OnProgress = p => HandleProgress(session, p)
        };
    }

    private void HandleProgress(ReviewSession session, MakerProgress p)
    {
        session.Timeline.Add(new TimelineEntry(p.Phase.ToString(), p.Message, DateTimeOffset.UtcNow));
        var previousPhase = session.CurrentPhase;
        session.CurrentPhase = p.Phase.ToString();
        
        // ─────────────────────────────────────────────────────────
        //  Stage Tracking - 阶段跟踪与日志
        // ─────────────────────────────────────────────────────────
        var tracker = _stageTrackers.GetOrAdd(session.Id, _ => new StageTracker());
        var currentPhase = p.Phase.ToString();
        
        // 阶段变更 - 输出上一阶段的完成日志（带详情）
        if (!string.IsNullOrEmpty(tracker.CurrentStage) && tracker.CurrentStage != currentPhase)
        {
            var endTime = DateTimeOffset.UtcNow;
            var duration = (long)(endTime - tracker.StageStartTime).TotalMilliseconds;
            
            // 构建详情
            var details = new StageDetails
            {
                Candidates = tracker.Candidates.Count > 0 ? [..tracker.Candidates] : null,
                WorkerOutputs = tracker.WorkerOutputs.Count > 0 ? [..tracker.WorkerOutputs] : null,
                WinnerContent = tracker.WinnerContent
            };
            
            SendEvent(session, new StageLogEvent
            {
                SessionId = session.Id,
                Stage = tracker.CurrentStage,
                Status = "completed",
                Summary = BuildStageSummary(tracker.CurrentStage, tracker),
                StartTime = tracker.StageStartTime,
                EndTime = endTime,
                DurationMs = duration,
                Stats = new StageStats
                {
                    LlmCalls = tracker.LlmCalls,
                    Tokens = tracker.Tokens,
                    WorkerCount = tracker.WorkerCount,
                    VotingRounds = tracker.VotingRounds,
                    ConsensusReached = tracker.ConsensusReached
                },
                Details = details
            });
            
            // 重置统计和详情
            tracker.Reset();
        }
        
        // 新阶段开始
        if (tracker.CurrentStage != currentPhase)
        {
            tracker.CurrentStage = currentPhase;
            tracker.StageStartTime = DateTimeOffset.UtcNow;
            
            SendEvent(session, new StageLogEvent
            {
                SessionId = session.Id,
                Stage = currentPhase,
                Status = "started",
                Summary = $"Stage '{currentPhase}' started",
                StartTime = tracker.StageStartTime,
                EndTime = tracker.StageStartTime
            });
        }

        // ─────────────────────────────────────────────────────────
        //  Progress Event - 基础进度
        // ─────────────────────────────────────────────────────────
        SendEvent(session, new Models.ProgressEvent
        {
            SessionId = session.Id,
            Phase = p.Phase.ToString(),
            Message = p.Message,
            ProgressPercent = session.ProgressPercent,
            Depth = p.Depth
        });

        // ─────────────────────────────────────────────────────────
        //  Phase Change Event - 阶段变更
        // ─────────────────────────────────────────────────────────
        SendEvent(session, new PhaseChangeEvent
        {
            SessionId = session.Id,
            TaskId = p.TaskId,
            OldPhase = previousPhase,
            NewPhase = p.Phase.ToString(),
            Message = p.Message
        });

        // ─────────────────────────────────────────────────────────
        //  Voting Events - 投票进度
        // ─────────────────────────────────────────────────────────
        if (p.Voting != null)
        {
            var v = p.Voting;
            tracker.VotingRounds = Math.Max(tracker.VotingRounds, v.Round);
            tracker.WorkerCount = Math.Max(tracker.WorkerCount, v.TotalVotes);
            
            // 记录投票统计为候选（简化版，没有完整内容）
            tracker.Candidates.Clear();
            tracker.Candidates.Add(new CandidateDetail
            {
                Id = "Leader",
                Preview = $"Leading candidate with {v.LeaderVotes} votes",
                Votes = v.LeaderVotes,
                IsWinner = v.LeaderVotes >= v.VotesNeeded
            });
            if (v.RunnerUpVotes > 0)
            {
                tracker.Candidates.Add(new CandidateDetail
                {
                    Id = "RunnerUp",
                    Preview = $"Runner-up candidate with {v.RunnerUpVotes} votes",
                    Votes = v.RunnerUpVotes,
                    IsWinner = false
                });
            }
            
            // 发送投票轮次事件
            SendEvent(session, new VotingRoundEvent
            {
                SessionId = session.Id,
                TaskId = p.TaskId,
                VotingType = v.Type.ToString(),
                Round = v.Round,
                VotesNeeded = v.VotesNeeded,
                ConsensusReached = v.LeaderVotes >= v.VotesNeeded,
                Candidates = 
                [
                    new VoteCandidateInfo
                    {
                        CandidateId = "Leader",
                        Votes = v.LeaderVotes,
                        IsLeader = true
                    },
                    new VoteCandidateInfo
                    {
                        CandidateId = "RunnerUp",
                        Votes = v.RunnerUpVotes,
                        IsLeader = false
                    }
                ]
            });
            
            // 共识达成
            if (v.LeaderVotes >= v.VotesNeeded)
            {
                tracker.ConsensusReached = true;
                
                SendEvent(session, new ConsensusEvent
                {
                    SessionId = session.Id,
                    Round = v.Round,
                    TotalVotes = v.TotalVotes,
                    VotesNeeded = v.VotesNeeded,
                    LeaderVotes = v.LeaderVotes,
                    Reached = true
                });
            }
        }
        
        // ─────────────────────────────────────────────────────────
        //  LLM Streaming Events - 实时 LLM 输出
        // ─────────────────────────────────────────────────────────
        if (p.StreamingToken != null)
        {
            var st = p.StreamingToken;
            
            // 首个 Token - Worker 开始 + LLM 调用开始
            if (st.IsFirstToken)
            {
                // 生成唯一的 CallId：{workerId}:{proposalId}
                // 避免不同 workers 生成相同 ProposalId（如 S1）导致 UI Map 覆盖
                var uniqueCallId = $"{st.WorkerId}:{st.ProposalId}";
                
                _logger.LogInformation(
                    "[STREAMING_FIRST] Worker={WorkerId}, ProposalId={ProposalId}, CallId={CallId}, Phase={Phase}",
                    st.WorkerId, st.ProposalId, uniqueCallId, p.Phase);
                
                SendEvent(session, new WorkerStartedEvent
                {
                    SessionId = session.Id,
                    WorkerId = st.WorkerId,
                    TaskId = p.TaskId,
                    Role = p.Phase.ToString(),
                    ProviderName = st.ProviderName
                });
                
                SendEvent(session, new LlmCallStartEvent
                {
                    SessionId = session.Id,
                    CallId = uniqueCallId,
                    WorkerId = st.WorkerId,
                    ProviderName = st.ProviderName,
                    SystemPrompt = st.SystemPrompt,
                    UserPrompt = st.UserPrompt,
                    Phase = p.Phase.ToString()
                });
            }
            
            // Streaming Token - 使用相同的唯一 CallId
            var streamCallId = $"{st.WorkerId}:{st.ProposalId}";
            SendEvent(session, new LlmStreamingEvent
            {
                SessionId = session.Id,
                CallId = streamCallId,
                WorkerId = st.WorkerId,
                Token = st.Token,
                AccumulatedContent = st.AccumulatedContent,
                TokenIndex = st.TokenIndex,
                IsFirstToken = st.IsFirstToken,
                IsLastToken = st.IsLastToken
            });
        }
        
        // ─────────────────────────────────────────────────────────
        //  LLM Proposal Complete - Worker 完成
        // ─────────────────────────────────────────────────────────
        if (p.Proposal != null)
        {
            var prop = p.Proposal;
            
            // 更新统计
            tracker.LlmCalls++;
            tracker.Tokens += prop.TotalTokens;
            
            // 获取正确的 Worker ID（从 Proposal 或 fallback 到 TaskId）
            var workerId = prop.WorkerId ?? p.TaskId;
            var uniqueCallId = $"{workerId}:{prop.ProposalId}";
            
            // ─────────────────────────────────────────────────────────
            //  确保 Worker 和 LLM Call 的 Start 事件被发送（补偿非 streaming 情况）
            //  如果 streaming 已经发送过 start 事件，前端会通过 Map 自动去重
            // ─────────────────────────────────────────────────────────
            _logger.LogInformation(
                "[PROPOSAL_COMPLETE] Worker={WorkerId}, ProposalId={ProposalId}, CallId={CallId}, Phase={Phase}, Tokens={Tokens}",
                workerId, prop.ProposalId, uniqueCallId, p.Phase, prop.TotalTokens);
            
            // 发送 WorkerStartedEvent（前端会通过 workerId 去重）
            SendEvent(session, new WorkerStartedEvent
            {
                SessionId = session.Id,
                WorkerId = workerId,
                TaskId = p.TaskId,
                Role = p.Phase.ToString(),
                ProviderName = prop.ProviderName
            });
            
            // 发送 LlmCallStartEvent（前端会通过 callId 去重）
            SendEvent(session, new LlmCallStartEvent
            {
                SessionId = session.Id,
                CallId = uniqueCallId,
                WorkerId = workerId,
                ProviderName = prop.ProviderName,
                Phase = p.Phase.ToString()
            });
            
            // 收集 Worker 输出详情
            tracker.WorkerOutputs.Add(new WorkerOutput
            {
                WorkerId = workerId,
                Preview = prop.Content?.Length > 200 ? prop.Content[..200] + "..." : prop.Content ?? "",
                Content = prop.Content ?? "",
                LatencyMs = prop.LatencyMs,
                Tokens = prop.TotalTokens,
                Success = prop.Success
            });
            
            // Worker 完成事件
            SendEvent(session, new WorkerCompletedEvent
            {
                SessionId = session.Id,
                WorkerId = workerId,
                TaskId = p.TaskId,
                Success = prop.Success,
                ContentPreview = prop.Content?.Length > 100 
                    ? prop.Content.Substring(0, 100) + "..." 
                    : prop.Content ?? "",
                Content = prop.Content ?? "",
                LatencyMs = prop.LatencyMs,
                TotalTokens = prop.TotalTokens
            });
            
            // LLM 调用完成事件 - 使用唯一的 CallId
            var completeCallId = uniqueCallId;
            SendEvent(session, new LlmCallCompleteEvent
            {
                SessionId = session.Id,
                CallId = completeCallId,
                WorkerId = workerId,
                Success = prop.Success,
                Content = prop.Content ?? "",
                Error = prop.Error,
                PromptTokens = prop.PromptTokens,
                CompletionTokens = prop.CompletionTokens,
                LatencyMs = prop.LatencyMs,
                ProviderName = prop.ProviderName,
                Phase = p.Phase.ToString()
            });
        }
    }
    
    /// <summary>
    /// 构建阶段总结信息。
    /// </summary>
    private static string BuildStageSummary(string stage, StageTracker tracker)
    {
        var parts = new List<string>();
        
        if (tracker.LlmCalls > 0)
            parts.Add($"{tracker.LlmCalls} LLM calls");
        
        if (tracker.Tokens > 0)
            parts.Add($"{tracker.Tokens:N0} tokens");
        
        if (tracker.WorkerCount > 0)
            parts.Add($"{tracker.WorkerCount} workers");
        
        if (tracker.VotingRounds > 0)
            parts.Add($"{tracker.VotingRounds} voting rounds");
        
        if (tracker.ConsensusReached)
            parts.Add("✓ consensus");
        
        var details = parts.Count > 0 ? string.Join(", ", parts) : "completed";
        
        return stage switch
        {
            "Starting" => $"Initialization complete. {details}",
            "Assessing" => $"Task complexity assessed. {details}",
            "Decomposing" => $"Task decomposed into subtasks. {details}",
            "Voting" => $"Voting consensus process. {details}",
            "Solving" => $"Atomic task solved. {details}",
            "Composing" => $"Results composed. {details}",
            "Streaming" => $"LLM streaming complete. {details}",
            "Completed" => $"Review completed successfully. {details}",
            "Failed" => $"Stage failed. {details}",
            _ => $"Stage '{stage}' completed. {details}"
        };
    }

    private string FormatReviewReport(ReviewSession session, MakerResult result)
    {
        return $"""
            # 📝 Paper Review Report
            
            **Title:** {session.Title}  
            **Authors:** {session.Authors}  
            **Venue:** {session.VenueType}  
            **Review Type:** {session.Type}  
            **Session ID:** {session.Id}  
            
            ---
            
            **Status:** {(result.Success ? "✓ Completed" : "✗ Failed")}  
            **Duration:** {session.Duration.TotalSeconds:F1}s  
            **LLM Calls:** {result.TotalLLMCalls}  
            **Total Tokens:** {result.TotalTokens:N0}
            
            ---
            
            {result.Content}
            """;
    }

    private void SendEvent(ReviewSession session, ReviewEvent evt)
    {
        session.EventChannel.Writer.TryWrite(evt);
    }

    private void SaveFile(ReviewSession session, string category, string fileName, string content)
    {
        session.Files.GetOrAdd(category, _ => new ConcurrentDictionary<string, string>())[fileName] = content;

        if (!string.IsNullOrEmpty(session.OutputDir))
        {
            try
            {
                var dir = Path.Combine(session.OutputDir, category);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, fileName), content);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save file: {Category}/{FileName}", category, fileName);
            }
        }
    }

    // ─────────────────────────────────────────────────────────
    //  状态查询
    // ─────────────────────────────────────────────────────────

    public object? GetStatus(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return new { status = "not_found" };

        return new
        {
            status = session.Status.ToString().ToLower(),
            sessionId = session.Id,
            session.Title,
            type = session.Type.ToString(),
            session.ProgressPercent,
            phase = session.CurrentPhase,
            session.TotalLlmCalls,
            session.TotalTokens,
            duration = session.Duration.TotalSeconds
        };
    }

    public object? GetResult(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return new { success = false, error = "Session not found" };

        var content = session.Files.TryGetValue("reports", out var reports) 
            ? reports.GetValueOrDefault("review_report.md") 
            : null;

        return new
        {
            success = session.Status == ReviewStatus.Completed,
            content,
            error = session.Error,
            session.TotalLlmCalls,
            session.TotalTokens,
            duration = session.Duration.TotalSeconds
        };
    }

    public IEnumerable<object> GetHistory(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return [];

        return session.Timeline.Select(e => new
        {
            phase = e.Phase,
            message = e.Message,
            timestamp = e.Timestamp
        });
    }

    public async IAsyncEnumerable<ReviewEvent> GetEventStreamAsync(
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
}
