using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Aevatar.CognitiveMesh.Abstractions;
using Aevatar.CognitiveMesh.Models;

namespace Aevatar.CognitiveMesh.Services;

// ============================================================
//  COGNITIVE MESH SERVICE
//  认知网格核心服务 - 项目管理与执行调度
// ============================================================

/// <summary>
/// 认知网格核心服务。
/// 统一管理 MAKER 和 UoT 等策略的项目执行。
/// </summary>
public sealed class CognitiveMeshService
{
    private readonly StrategyRegistry _registry;
    private readonly ILogger<CognitiveMeshService> _logger;
    private readonly ConcurrentDictionary<string, MeshRun> _runs = new();
    private readonly ConcurrentDictionary<string, MeshProject> _dynamicProjects = new();

    // ─────────────────────────────────────────────────────────
    //  内置项目
    // ─────────────────────────────────────────────────────────
    private static readonly MeshProject[] BuiltInProjects =
    [
        // ═══════════════════════════════════════════════════════════
        //  MAKER 策略项目
        // ═══════════════════════════════════════════════════════════
        
        new MeshProject
        {
            Id = "paper-review",
            Name = "论文审稿",
            Description = "多 Agent 协作审阅论文，提供修改建议直到达到发表水平",
            Icon = "📝",
            Strategy = StrategyKind.Maker,
            Task = "Review and improve the academic paper for publication quality.",
            Options = ReasoningOptions.ForMaker(MakerReliability.High, 100, 500_000)
        },

        // ═══════════════════════════════════════════════════════════
        //  C-UoT 组合式项目 (重组已有思想)
        // ═══════════════════════════════════════════════════════════
        
        new MeshProject
        {
            Id = "bridge-traffic",
            Name = "桥梁交通",
            Description = "设计单车道桥梁的双向交通管理机制",
            Icon = "🌉",
            Strategy = StrategyKind.UotCombinational,
            Task = "Design a mechanism for managing two-way traffic on a single-lane bridge where vehicles from both directions need to cross safely without collision.",
            Options = ReasoningOptions.ForUotCombinational("transportation, distributed systems, resource scheduling")
        },

        new MeshProject
        {
            Id = "bookstore-revival",
            Name = "书店复兴",
            Description = "帮助传统书店在电商时代重获增长",
            Icon = "📚",
            Strategy = StrategyKind.UotCombinational,
            Task = "How can a traditional physical bookstore regain growth and relevance in the era of e-commerce and digital books?",
            Options = ReasoningOptions.ForUotCombinational("retail, business strategy, community")
        },

        // ═══════════════════════════════════════════════════════════
        //  E-UoT 探索式项目 (发现域外思想)
        // ═══════════════════════════════════════════════════════════
        
        new MeshProject
        {
            Id = "senior-fitness-explore",
            Name = "老年健身·探索",
            Description = "探索未知领域，为 60+ 用户发现创新健身功能",
            Icon = "🔭",
            Strategy = StrategyKind.UotExploratory,
            Task = "Design an innovative fitness app feature specifically for users aged 60+ that encourages regular physical activity while being safe and engaging. Look for inspiration from unexpected domains.",
            Options = ReasoningOptions.ForUotExploratory(
                domainHint: "health, gamification, accessibility, unexpected domains",
                maxOutsideThoughts: 15,
                explorationDirections: 4)
        },

        new MeshProject
        {
            Id = "remote-collaboration-explore",
            Name = "远程协作·探索",
            Description = "探索新概念解决 Zoom 疲劳问题",
            Icon = "🔬",
            Strategy = StrategyKind.UotExploratory,
            Task = "Design a new approach to remote team collaboration that solves the problem of 'Zoom fatigue' while maintaining team cohesion and productivity. Explore concepts from diverse fields.",
            Options = ReasoningOptions.ForUotExploratory(
                domainHint: "workplace, communication, psychology, game design, theater",
                maxOutsideThoughts: 12,
                explorationDirections: 5)
        },

        // ═══════════════════════════════════════════════════════════
        //  T-UoT 变革式项目 (挑战规则本身)
        // ═══════════════════════════════════════════════════════════
        
        new MeshProject
        {
            Id = "bookstore-transform",
            Name = "书店变革",
            Description = "挑战关于书店的隐藏假设，探索颠覆性商业模式",
            Icon = "💥",
            Strategy = StrategyKind.UotTransformative,
            Task = "Challenge hidden assumptions about what a bookstore must be. How can we fundamentally reimagine the concept of a bookstore for the digital age?",
            Options = ReasoningOptions.ForUotTransformative(
                domainHint: "retail, business strategy, community, digital transformation",
                maxRuleSets: 4,
                mutationsPerSet: 3,
                minRadicality: 0.6f)
        },

        new MeshProject
        {
            Id = "education-transform",
            Name = "教育变革",
            Description = "挑战传统教育的隐藏假设，重新定义学习",
            Icon = "🚀",
            Strategy = StrategyKind.UotTransformative,
            Task = "Challenge hidden assumptions about education. What if learning didn't require classrooms, grades, or even teachers in the traditional sense?",
            Options = ReasoningOptions.ForUotTransformative(
                domainHint: "education, technology, cognitive science, game design",
                maxRuleSets: 3,
                mutationsPerSet: 4,
                minRadicality: 0.7f)
        },

        new MeshProject
        {
            Id = "healthcare-transform",
            Name = "医疗变革",
            Description = "挑战医疗服务的根本假设，探索未来健康模式",
            Icon = "🏥",
            Strategy = StrategyKind.UotTransformative,
            Task = "Challenge the hidden assumptions about healthcare delivery. What if health wasn't about treating illness but something fundamentally different?",
            Options = ReasoningOptions.ForUotTransformative(
                domainHint: "healthcare, prevention, technology, community health",
                maxRuleSets: 3,
                mutationsPerSet: 3,
                minRadicality: 0.65f)
        }
    ];

    private IEnumerable<MeshProject> AllProjects => BuiltInProjects.Concat(_dynamicProjects.Values);

    public CognitiveMeshService(StrategyRegistry registry, ILogger<CognitiveMeshService> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────
    //  项目管理
    // ─────────────────────────────────────────────────────────

    public IEnumerable<object> GetProjects() =>
        AllProjects.Select(p => new
        {
            p.Id,
            p.Name,
            p.Description,
            p.Icon,
            strategy = p.Strategy.ToString(),
            strategyDisplayName = p.Strategy.GetDisplayName(),
            isDynamic = _dynamicProjects.ContainsKey(p.Id)
        });

    public object CreateProject(string configJson)
    {
        try
        {
            var config = JsonSerializer.Deserialize<ProjectConfig>(configJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new ArgumentException("无法解析项目配置");

            var projectId = $"custom_{Guid.NewGuid():N}"[..16];

            var project = new MeshProject
            {
                Id = projectId,
                Name = config.Name ?? "Custom Project",
                Description = config.Description ?? "",
                Icon = config.Icon ?? "🔬",
                Strategy = Enum.TryParse<StrategyKind>(config.Strategy, true, out var s) ? s : StrategyKind.Maker,
                Task = config.Task ?? throw new ArgumentException("Task is required"),
                Options = BuildOptions(config)
            };

            _dynamicProjects[projectId] = project;

            _logger.LogInformation("Created project: {ProjectId} - {Name} with strategy {Strategy}",
                projectId, project.Name, project.Strategy);

            return new
            {
                success = true,
                projectId,
                name = project.Name,
                strategy = project.Strategy.ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create project");
            return new { success = false, error = ex.Message };
        }
    }

    private static ReasoningOptions BuildOptions(ProjectConfig config)
    {
        return new ReasoningOptions
        {
            ProviderName = config.ProviderName,
            MaxLlmCalls = config.MaxLlmCalls ?? 500,
            MaxTokens = config.MaxTokens ?? 2_000_000,
            MaxDuration = TimeSpan.FromMinutes(config.MaxDurationMinutes ?? 30),
            MakerReliability = Enum.TryParse<MakerReliability>(config.Reliability, true, out var r) ? r : MakerReliability.Medium,
            UotDomainHint = config.DomainHint,
            UotMaxAnalogies = config.MaxAnalogies ?? 5,
            UotMaxCandidates = config.MaxCandidates ?? 10,
            Context = config.Context
        };
    }

    public bool DeleteProject(string projectId)
    {
        if (BuiltInProjects.Any(p => p.Id == projectId))
            return false;
        return _dynamicProjects.TryRemove(projectId, out _);
    }

    // ─────────────────────────────────────────────────────────
    //  运行管理
    // ─────────────────────────────────────────────────────────

    public async Task<object> StartRunAsync(string projectId, CancellationToken ct)
    {
        var project = AllProjects.FirstOrDefault(p => p.Id == projectId);
        if (project == null)
            return new { success = false, error = "Project not found" };

        var strategy = _registry.Get(project.Strategy);
        if (strategy == null)
            return new { success = false, error = $"Strategy {project.Strategy} not available" };

        if (_runs.TryGetValue(projectId, out var existing) && existing.Status == MeshRunStatus.Running)
            return new { success = false, error = "Already running", runId = existing.RunId };

        var run = new MeshRun
        {
            ProjectId = projectId,
            ProjectName = project.Name,
            Strategy = project.Strategy
        };
        _runs[projectId] = run;

        // 设置输出目录
        var outputBase = Path.Combine(Directory.GetCurrentDirectory(), "output", projectId, run.RunId);
        Directory.CreateDirectory(outputBase);
        run.OutputDir = outputBase;

        _logger.LogInformation("Starting {Strategy} run {RunId} for project {Project}",
            project.Strategy, run.RunId, projectId);

        // 后台执行
        _ = ExecuteAsync(run, project, strategy, CancellationToken.None);

        return new { success = true, runId = run.RunId, strategy = project.Strategy.ToString() };
    }

    private async Task ExecuteAsync(MeshRun run, MeshProject project, IReasoningStrategy strategy, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 创建进度回调
            var progress = new Progress<ReasoningProgress>(p =>
            {
                run.Timeline.Add(new TimelineEntry(p.Phase, p.Message ?? "", DateTimeOffset.UtcNow));

                // 更新运行状态
                run.CurrentPhase = p.Phase;
                run.ProgressPercent = p.ProgressPercent;
                run.Depth = p.Depth ?? run.Depth;
                run.TotalLlmCalls = p.TotalLlmCalls ?? run.TotalLlmCalls;
                run.TotalTokens = (p.TotalPromptTokens ?? 0) + (p.TotalCompletionTokens ?? 0);

                // 发送 SSE 事件
                SendEvent(run, new ProgressEvent
                {
                    RunId = run.RunId,
                    Phase = p.Phase,
                    Message = p.Message,
                    TaskId = p.TaskId,
                    Depth = p.Depth,
                    ProgressPercent = p.ProgressPercent
                });

                // MAKER 特有事件
                if (p.Voting != null)
                {
                    SendEvent(run, new VotingEvent
                    {
                        RunId = run.RunId,
                        TaskId = p.TaskId ?? "",
                        VotingType = p.Voting.Type,
                        Round = p.Voting.Round,
                        TotalVotes = p.Voting.TotalVotes,
                        VotesNeeded = p.Voting.VotesNeeded,
                        LeaderVotes = p.Voting.LeaderVotes,
                        RunnerUpVotes = p.Voting.RunnerUpVotes,
                        ClusterCount = p.Voting.ClusterCount,
                        UsedSemanticClustering = p.Voting.UsedSemanticClustering,
                        Success = p.Voting.LeaderVotes - p.Voting.RunnerUpVotes >= p.Voting.VotesNeeded
                    });
                }

                if (p.Proposal != null)
                {
                    SendEvent(run, new ProposalEvent
                    {
                        RunId = run.RunId,
                        TaskId = p.TaskId ?? "",
                        Content = p.Proposal.Content,
                        Success = p.Proposal.Success,
                        Error = p.Proposal.Error,
                        PromptTokens = p.Proposal.PromptTokens,
                        CompletionTokens = p.Proposal.CompletionTokens,
                        ProviderName = p.Proposal.ProviderName
                    });
                }

                if (p.StreamingToken != null)
                {
                    SendEvent(run, new StreamingEvent
                    {
                        RunId = run.RunId,
                        TaskId = p.TaskId ?? "",
                        WorkerId = p.StreamingToken.WorkerId,
                        ProposalId = p.StreamingToken.ProposalId,
                        Token = p.StreamingToken.Token,
                        AccumulatedContent = p.StreamingToken.AccumulatedContent,
                        TokenIndex = p.StreamingToken.TokenIndex,
                        IsFirstToken = p.StreamingToken.IsFirstToken,
                        IsLastToken = p.StreamingToken.IsLastToken,
                        ProviderName = p.StreamingToken.ProviderName,
                        SystemPrompt = p.StreamingToken.SystemPrompt,
                        UserPrompt = p.StreamingToken.UserPrompt
                    });
                }
            });

            // ───────────────────────────────────────────────────────
            //  动态加载任务内容（论文审稿等需要外部数据的项目）
            // ───────────────────────────────────────────────────────
            var task = project.Task;
            var options = project.Options;

            if (project.Id == "paper-review")
            {
                // 尝试加载论文内容
                var paperPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "articles", "minimal_axiomatic_ontology_universe.md");
                if (File.Exists(paperPath))
                {
                    var paperContent = await File.ReadAllTextAsync(paperPath, ct);
                    task = $"""
                        Review and improve the following academic paper for top-tier AI conference publication.
                        
                        Provide specific, actionable suggestions for:
                        1. Structural improvements (organization, flow, section balance)
                        2. Argument clarity and logical rigor
                        3. Mathematical presentation and notation consistency
                        4. Related work coverage and positioning
                        5. Writing quality (clarity, conciseness, academic tone)
                        6. Figures and diagrams (if any)
                        
                        For each issue found:
                        - Quote the problematic text
                        - Explain why it's problematic
                        - Provide a concrete revision suggestion
                        
                        ═══════════════════════════════════════════════════════════════
                        PAPER TO REVIEW
                        ═══════════════════════════════════════════════════════════════
                        
                        {paperContent}
                        
                        ═══════════════════════════════════════════════════════════════
                        END OF PAPER
                        ═══════════════════════════════════════════════════════════════
                        """;
                    
                    _logger.LogInformation("[paper-review] Loaded paper content ({Length} chars)", paperContent.Length);
                }
                else
                {
                    _logger.LogWarning("[paper-review] Paper file not found at {Path}", paperPath);
                }
            }

            // 执行策略
            var result = await strategy.ExecuteAsync(task, options, progress, ct);

            stopwatch.Stop();
            run.Result = result;
            run.Status = result.Success ? MeshRunStatus.Completed : MeshRunStatus.Failed;
            run.Duration = stopwatch.Elapsed;
            run.TotalLlmCalls = result.TotalLlmCalls;
            run.TotalTokens = result.TotalTokens;

            _logger.LogInformation("[{Project}] {Strategy} completed. Success={Success}, Duration={Duration}s",
                run.ProjectId, run.Strategy, result.Success, stopwatch.Elapsed.TotalSeconds);

            // 保存最终结果
            if (!string.IsNullOrEmpty(result.Content))
            {
                SaveFile(run, "artifacts", "final_report.md", $"""
                    # 📄 Final Report

                    **Project:** {run.ProjectName}  
                    **Strategy:** {run.Strategy.GetDisplayName()}  
                    **Run ID:** {run.RunId}  
                    **Status:** {(result.Success ? "✓ Success" : "✗ Failed")}  
                    **Duration:** {stopwatch.Elapsed.TotalSeconds:F1}s  
                    **LLM Calls:** {result.TotalLlmCalls}  
                    **Total Tokens:** {result.TotalTokens:N0}

                    ---

                    {result.Content}
                    """);
            }

            // 发送结果事件
            SendEvent(run, new ResultEvent
            {
                RunId = run.RunId,
                Success = result.Success,
                Content = result.Content,
                Error = result.Error,
                TotalTokens = result.TotalTokens,
                PromptTokens = (int)result.PromptTokens,
                CompletionTokens = (int)result.CompletionTokens,
                TotalLlmCalls = result.TotalLlmCalls
            });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            run.Status = MeshRunStatus.Failed;
            run.Duration = stopwatch.Elapsed;
            run.Error = ex.Message;

            _logger.LogError(ex, "[{Project}] {Strategy} failed", run.ProjectId, run.Strategy);

            SendEvent(run, new ErrorEvent
            {
                RunId = run.RunId,
                Message = ex.Message,
                StackTrace = ex.StackTrace
            });
        }
        finally
        {
            run.EventChannel.Writer.Complete();
        }
    }

    private void SendEvent(MeshRun run, MeshEvent evt)
    {
        run.EventChannel.Writer.TryWrite(evt);
    }

    private void SaveFile(MeshRun run, string category, string fileName, string content)
    {
        run.Files.GetOrAdd(category, _ => new ConcurrentDictionary<string, string>())[fileName] = content;

        if (!string.IsNullOrEmpty(run.OutputDir))
        {
            try
            {
                var dir = Path.Combine(run.OutputDir, category);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, fileName), content);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write file: {Category}/{FileName}", category, fileName);
            }
        }

        SendEvent(run, new FileEvent
        {
            RunId = run.RunId,
            Category = category,
            FileName = fileName,
            Message = $"Generated: {category}/{fileName}"
        });
    }

    // ─────────────────────────────────────────────────────────
    //  状态查询
    // ─────────────────────────────────────────────────────────

    public object? GetStatus(string projectId)
    {
        if (!_runs.TryGetValue(projectId, out var run))
            return new { status = "idle", runId = (string?)null };

        return new
        {
            status = run.Status.ToString().ToLower(),
            runId = run.RunId,
            strategy = run.Strategy.ToString(),
            elapsed = run.Duration.TotalSeconds,
            progress = run.ProgressPercent,
            phase = run.CurrentPhase
        };
    }

    public object? GetSnapshot(string projectId)
    {
        if (!_runs.TryGetValue(projectId, out var run))
            return new { status = "idle" };

        return new
        {
            status = run.Status.ToString().ToLower(),
            runId = run.RunId,
            strategy = run.Strategy.ToString(),
            strategyDisplayName = run.Strategy.GetDisplayName(),
            result = run.Result?.Content,
            success = run.Result?.Success,
            llmCalls = run.TotalLlmCalls,
            totalTokens = run.TotalTokens,
            duration = run.Duration.TotalSeconds,
            depth = run.Depth,
            phase = run.CurrentPhase,
            progress = run.ProgressPercent
        };
    }

    public IEnumerable<object> GetTimeline(string projectId)
    {
        if (!_runs.TryGetValue(projectId, out var run))
            return [];

        return run.Timeline.Select(e => new { phase = e.Phase, message = e.Message, timestamp = e.Timestamp });
    }

    public async IAsyncEnumerable<MeshEvent> GetEventStreamAsync(
        string projectId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!_runs.TryGetValue(projectId, out var run))
            yield break;

        await foreach (var evt in run.EventChannel.Reader.ReadAllAsync(ct))
        {
            yield return evt;
        }
    }

    public IEnumerable<object> GetFiles(string projectId)
    {
        if (!_runs.TryGetValue(projectId, out var run))
            return [];

        var files = new List<object>();
        foreach (var (category, fileDict) in run.Files)
        {
            foreach (var name in fileDict.Keys)
            {
                files.Add(new { category, name, path = $"{category}/{name}" });
            }
        }
        return files.OrderBy(f => ((dynamic)f).category).ThenBy(f => ((dynamic)f).name);
    }

    public string? GetFileContent(string projectId, string category, string name)
    {
        if (!_runs.TryGetValue(projectId, out var run))
            return null;

        if (!run.Files.TryGetValue(category, out var files))
            return null;

        return files.GetValueOrDefault(name);
    }

    // ─────────────────────────────────────────────────────────
    //  示例问题
    // ─────────────────────────────────────────────────────────

    public IEnumerable<object> GetSampleProblems() =>
        BuiltInProjects
            .Where(p => p.Strategy.IsUoT())
            .Select(p => new
            {
                p.Id,
                title = p.Name,
                problem = p.Task,
                domainHint = p.Options.UotDomainHint,
                strategy = p.Strategy.ToString(),
                strategyDisplayName = p.Strategy.GetDisplayName(),
                strategyDescription = p.Strategy.GetDescription(),
                category = p.Strategy switch
                {
                    StrategyKind.UotCombinational => "Combinational",
                    StrategyKind.UotExploratory => "Exploratory",
                    StrategyKind.UotTransformative => "Transformative",
                    _ => "Creative"
                }
            });
}

