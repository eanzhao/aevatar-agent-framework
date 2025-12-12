using System.Runtime.CompilerServices;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;

namespace Aevatar.PaperReview.Tests.Mocks;

// ============================================================
//  MOCK LLM PROVIDER
//  模拟 LLM 返回，用于测试 Paper Review 流程
// ============================================================

/// <summary>
/// Mock LLM Provider - 根据 prompt 内容返回预设的响应。
/// 支持 MAKER 工作流的各个阶段。
/// </summary>
public sealed class MockLLMProvider : IAevatarLLMProvider
{
    private readonly Dictionary<string, string> _responses = new();
    private readonly List<AevatarLLMRequest> _requestHistory = [];
    private int _callCount;

    /// <summary>
    /// 所有收到的请求历史。
    /// </summary>
    public IReadOnlyList<AevatarLLMRequest> RequestHistory => _requestHistory;

    /// <summary>
    /// LLM 调用次数。
    /// </summary>
    public int CallCount => _callCount;

    /// <summary>
    /// 添加基于关键字的响应规则。
    /// </summary>
    public MockLLMProvider WithResponse(string promptKeyword, string response)
    {
        _responses[promptKeyword.ToLowerInvariant()] = response;
        return this;
    }

    /// <summary>
    /// 配置 MAKER 工作流的标准响应。
    /// </summary>
    public MockLLMProvider WithMakerWorkflowResponses()
    {
        // check_atomic: 判断任务是否原子
        _responses["atomic"] = """
            {
                "is_atomic": false,
                "reason": "The paper review task requires multiple dimensions of analysis",
                "suggested_subtasks": ["technical_analysis", "novelty_assessment", "presentation_quality"]
            }
            """;

        // decompose: 分解任务
        // 注意：真实 prompt 不一定包含 “decompose” 字样，用更稳定的关键短语匹配。
        _responses["break down the following complex task"] = """
            [
                {"id": "tech", "description": "Analyze technical soundness and methodology"},
                {"id": "novelty", "description": "Assess novelty and contribution"},
                {"id": "presentation", "description": "Evaluate clarity and presentation"}
            ]
            """;

        // solve: 解决原子任务
        _responses["solve"] = """
            ## Analysis Result
            
            The paper presents a well-structured approach with solid methodology.
            
            **Strengths:**
            - Clear problem formulation
            - Sound experimental design
            - Comprehensive evaluation
            
            **Weaknesses:**
            - Limited discussion of limitations
            - Could benefit from more baselines
            
            **Score: 7/10**
            """;

        // compose: 合成结果
        _responses["compose"] = """
            # Paper Review Summary
            
            ## Overall Assessment
            This paper makes a meaningful contribution to the field.
            
            ## Technical Soundness: 7/10
            The methodology is sound with minor concerns.
            
            ## Novelty: 6/10
            Incremental but solid contribution.
            
            ## Presentation: 8/10
            Well-written and clear.
            
            ## Recommendation: Weak Accept
            """;

        // vote: 投票
        _responses["vote"] = """
            {
                "selected": 0,
                "reason": "This proposal provides the most comprehensive analysis"
            }
            """;

        // 通用 review 响应
        _responses["review"] = """
            ## Paper Review
            
            **Summary:** This paper addresses an important problem with a novel approach.
            
            **Strengths:**
            1. Clear motivation and problem statement
            2. Sound technical approach
            3. Comprehensive experiments
            
            **Weaknesses:**
            1. Limited theoretical analysis
            2. Missing ablation studies
            
            **Questions for Authors:**
            1. How does the method scale to larger datasets?
            2. What are the computational requirements?
            
            **Overall Score: 6/10**
            **Confidence: 4/5**
            **Recommendation: Borderline Accept**
            """;

        return this;
    }

    public Task<AevatarLLMResponse> GenerateAsync(
        AevatarLLMRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        _requestHistory.Add(request);

        var prompt = (request.UserPrompt + " " + (request.SystemPrompt ?? "")).ToLowerInvariant();

        // ============================================================
        //  Structured prompts MUST win over generic keywords
        //
        //  NOTE:
        //  - Maker-v2 的 generator prompt 会内嵌原始 task（包含 "review" 等泛词）
        //  - 如果靠 Dictionary 遍历的“偶然顺序”做 Contains 匹配，极易被泛词抢先命中
        //  - 这里显式做高优先级分流：先匹配结构化输出（json/json_array），再退回关键词表
        // ============================================================

        AevatarLLMResponse Build(string content) => new()
        {
            Content = content,
            AevatarStopReason = AevatarStopReason.Complete,
            Usage = new AevatarTokenUsage
            {
                PromptTokens = prompt.Length / 4,
                CompletionTokens = content.Length / 4,
                TotalTokens = (prompt.Length + content.Length) / 4
            },
            ModelName = "mock-model"
        };

        // ─────────────────────────────────────────────────────────
        // check_atomic: 需要“可终止递归”的行为
        // - 顶层论文评审：判定为 COMPLEX（触发分解）
        // - 子任务（例如“Assess novelty ...”）：判定为 ATOMIC（触发 solve），避免无限递归
        //
        // 通过检测 check_atomic 专属字段 is_atomic 来区分其它步骤的 prompt。
        // ─────────────────────────────────────────────────────────
        if (prompt.Contains("\"is_atomic\"") || prompt.Contains("is_atomic"))
        {
            // 只根据 TASK TO ANALYZE 这段来判断是否是“顶层论文评审”。
            // 注意：子任务 workflow_call 会把原始论文内容塞进 CONTEXT（其中也包含 “please review...”），
            // 如果直接全局 Contains，会导致子任务永远被判定为 COMPLEX → 无限递归直到 max_depth。
            static string ExtractTaskToAnalyze(string p)
            {
                var start = p.IndexOf("task to analyze:", StringComparison.OrdinalIgnoreCase);
                if (start < 0) return p;
                start = p.IndexOf('\n', start);
                if (start < 0) return p;

                var end = p.IndexOf("context:", start, StringComparison.OrdinalIgnoreCase);
                if (end < 0)
                    end = p.IndexOf("response format", start, StringComparison.OrdinalIgnoreCase);
                if (end < 0) end = p.Length;

                return p.Substring(start, end - start);
            }

            var taskToAnalyze = ExtractTaskToAnalyze(prompt);
            var isTopLevelPaperReview = taskToAnalyze.Contains("please review the following paper", StringComparison.OrdinalIgnoreCase);

            var content = isTopLevelPaperReview
                ? """
                  {
                    "is_atomic": false,
                    "reasoning": "A full paper review requires multiple dimensions of analysis; decompose first."
                  }
                  """
                : """
                  {
                    "is_atomic": true,
                    "reasoning": "This is a focused single-aspect subtask; solve directly."
                  }
                  """;

            return Task.FromResult(new AevatarLLMResponse
            {
                Content = content,
                AevatarStopReason = AevatarStopReason.Complete,
                Usage = new AevatarTokenUsage
                {
                    PromptTokens = prompt.Length / 4,
                    CompletionTokens = content.Length / 4,
                    TotalTokens = (prompt.Length + content.Length) / 4
                },
                ModelName = "mock-model"
            });
        }

        // decompose generator (json_array)
        if (prompt.Contains("break down the following complex task") &&
            (prompt.Contains("json array") || prompt.Contains("format your response as a json array")))
        {
            return Task.FromResult(Build(_responses["break down the following complex task"]));
        }

        // solve_atomic generator (text)
        if (prompt.Contains("solve the following task directly"))
        {
            return Task.FromResult(Build(_responses["solve"]));
        }

        // compose generator (text)
        if (prompt.Contains("compose the following subtask solutions"))
        {
            return Task.FromResult(Build(_responses["compose"]));
        }

        // vote selection (json)
        if (prompt.Contains("\"selected\"") && prompt.Contains("\"reason\""))
        {
            return Task.FromResult(Build(_responses["vote"]));
        }

        // 根据 prompt 内容匹配响应
        foreach (var (keyword, response) in _responses)
        {
            if (prompt.Contains(keyword))
            {
                return Task.FromResult(Build(response));
            }
        }

        // 默认响应
        return Task.FromResult(Build("This is a mock response for testing purposes."));
    }

    public async IAsyncEnumerable<AevatarLLMToken> GenerateStreamAsync(
        AevatarLLMRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GenerateAsync(request, cancellationToken);
        var content = response.Content;

        // 模拟流式输出：每次输出 20 个字符
        var chunkSize = 20;
        var index = 0;

        for (var i = 0; i < content.Length; i += chunkSize)
        {
            var chunk = content.Substring(i, Math.Min(chunkSize, content.Length - i));
            yield return new AevatarLLMToken
            {
                Content = chunk,
                Index = index++,
                IsComplete = i + chunkSize >= content.Length
            };

            // 模拟网络延迟
            await Task.Delay(10, cancellationToken);
        }
    }
}
