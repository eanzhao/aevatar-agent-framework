using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Cognitive.Messages;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Template;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Cognitive.Agents;

// ============================================================
//  Cognitive Worker Agent
//  真正执行 LLM 调用的 Actor
//  每个 Worker 独立运行在 Actor Runtime 上
// ============================================================

/// <summary>
/// Cognitive Worker Agent - 实际执行 LLM 调用
/// 
/// 架构：
/// - Coordinator 通过 Protobuf 事件分发任务
/// - Worker 独立执行，Actor Runtime 调度
/// - 完成后发送 Protobuf 事件回 Coordinator
/// 
/// 这是真正的 Actor 并行，不是进程内伪并发
/// </summary>
public class CognitiveWorkerGAgent : AIGAgentBase<CognitiveWorkerState>
{
    private readonly TemplateEngine _templateEngine = new();
    private readonly OutputParserFactory _parserFactory = new();

    public CognitiveWorkerGAgent()
    {
    }

    public CognitiveWorkerGAgent(Guid id) : base(id)
    {
    }

    // ============================================================
    //  生命周期
    // ============================================================

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        // 初始化 Worker 状态
        CustomState.WorkerId = Id.ToString("N")[..8];
        CustomState.Status = WorkerStatus.WsIdle;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult(
            $"CognitiveWorker [{CustomState.WorkerId}] - Status: {CustomState.Status}");
    }

    // ============================================================
    //  事件处理
    // ============================================================

    /// <summary>
    /// 处理执行步骤请求 (Protobuf 事件)
    /// </summary>
    [EventHandler]
    public async Task HandleExecuteStepRequest(ExecuteStepRequestEvent request)
    {
        Logger.LogDebug("Worker {WorkerId} received step request: {StepId}",
            CustomState.WorkerId, request.StepId);

        CustomState.Status = WorkerStatus.WsExecuting;
        CustomState.CurrentStepId = request.StepId;

        var startTime = DateTime.UtcNow;

        try
        {
            var result = await ExecuteStepAsync(request);

            CustomState.Status = WorkerStatus.WsIdle;
            CustomState.TotalStepsCompleted++;

            // 发送完成事件给 Coordinator (向上传播)
            await PublishAsync(new StepCompletedEventProto
            {
                RequestId = request.RequestId,
                StepId = request.StepId,
                WorkerId = CustomState.WorkerId,
                Success = result.Success,
                Result = result.Value?.ToString() ?? "",
                Error = result.Error ?? "",
                TokensUsed = result.TokensUsed,
                LlmCalls = result.LlmCalls,
                DurationMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds
            }, EventDirection.Up);
        }
        catch (Exception ex)
        {
            CustomState.Status = WorkerStatus.WsError;

            await PublishAsync(new StepCompletedEventProto
            {
                RequestId = request.RequestId,
                StepId = request.StepId,
                WorkerId = CustomState.WorkerId,
                Success = false,
                Error = ex.Message,
                DurationMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds
            }, EventDirection.Up);
        }
    }

    // ============================================================
    //  步骤执行
    // ============================================================

    private async Task<PrimitiveResult> ExecuteStepAsync(ExecuteStepRequestEvent request)
    {
        return request.StepType switch
        {
            "llm_call" => await ExecuteLlmCallAsync(request),
            _ => PrimitiveResult.Fail($"Worker does not support step type: {request.StepType}")
        };
    }

    private async Task<PrimitiveResult> ExecuteLlmCallAsync(ExecuteStepRequestEvent request)
    {
        // 从 Protobuf Value 转换参数
        var parameters = ConvertFromProtoMap(request.Parameters);
        var variables = ConvertFromProtoMap(request.Variables);

        // 解析参数
        var prompt = parameters.GetValueOrDefault("prompt")?.ToString() ?? "";
        var systemPrompt = parameters.GetValueOrDefault("system")?.ToString();
        var outputType = parameters.GetValueOrDefault("output")?.ToString() ?? "text";
        const int MaxContentLength = 102400;

        // 渲染模板
        prompt = _templateEngine.Render(prompt, variables);
        if (systemPrompt != null)
        {
            systemPrompt = _templateEngine.Render(systemPrompt, variables);
        }

        // 调用 LLM（优先流式）
        var llmRequest = new AevatarLLMRequest
        {
            SystemPrompt = systemPrompt,
            UserPrompt = prompt
        };

        var modelInfo = await LLMProvider.GetModelInfoAsync();
        var supportsStreaming = modelInfo.SupportsStreaming;

        var totalPromptTokens = 0;
        var totalCompletionTokens = 0;
        var finalContent = string.Empty;
        var parsedValue = (object?)null;

        if (supportsStreaming)
        {
            var sb = new System.Text.StringBuilder();
            var tokenIndex = 0;
            await foreach (var token in LLMProvider.GenerateStreamAsync(llmRequest))
            {
                var content = token.Content ?? string.Empty;
                if (string.IsNullOrEmpty(content) && !token.IsComplete)
                    continue;

                sb.Append(content);
                finalContent = sb.ToString();

                // 流式上报（Success=false 表示中间态）
                await PublishAsync(new StepCompletedEventProto
                {
                    RequestId = request.RequestId,
                    StepId = request.StepId,
                    WorkerId = CustomState.WorkerId,
                    Success = false,
                    Result = finalContent,
                    Error = "",
                    TokensUsed = tokenIndex, // 简单用 token 序号
                    LlmCalls = 0,
                    DurationMs = 0
                }, EventDirection.Up);

                tokenIndex++;

                if (token.IsComplete)
                {
                    totalCompletionTokens = tokenIndex;
                    break;
                }
            }

            // Red-flag：长度
            if (finalContent.Length > MaxContentLength)
            {
                return new PrimitiveResult { Success = false, Error = $"redflag-length>{MaxContentLength}" };
            }

            // 解析输出
            var parser = _parserFactory.Create(outputType);
            parsedValue = parser.Parse(finalContent);
            if (parsedValue == null)
            {
                return new PrimitiveResult { Success = false, Error = "redflag-parse-null" };
            }
        }
        else
        {
            var response = await LLMProvider.GenerateAsync(llmRequest);
            finalContent = response.Content ?? string.Empty;
            totalPromptTokens = response.Usage?.PromptTokens ?? 0;
            totalCompletionTokens = response.Usage?.CompletionTokens ?? 0;

            if (finalContent.Length > MaxContentLength)
            {
                return new PrimitiveResult { Success = false, Error = $"redflag-length>{MaxContentLength}" };
            }

            var parser = _parserFactory.Create(outputType);
            parsedValue = parser.Parse(finalContent);
            if (parsedValue == null)
            {
                return new PrimitiveResult { Success = false, Error = "redflag-parse-null" };
            }
        }

        return new PrimitiveResult
        {
            Success = true,
            Value = parsedValue,
            TokensUsed = totalPromptTokens + totalCompletionTokens,
            PromptTokens = totalPromptTokens,
            CompletionTokens = totalCompletionTokens,
            LlmCalls = 1,
            SystemPrompt = systemPrompt,
            UserPrompt = prompt,
            AssistantResponse = finalContent
        };
    }

    // ============================================================
    //  辅助方法
    // ============================================================

    /// <summary>
    /// 将 Protobuf Value Map 转换为普通字典
    /// </summary>
    private static Dictionary<string, object> ConvertFromProtoMap(
        Google.Protobuf.Collections.MapField<string, Value> protoMap)
    {
        var result = new Dictionary<string, object>();

        foreach (var (key, value) in protoMap)
        {
            result[key] = ConvertFromProtoValue(value);
        }

        return result;
    }

    /// <summary>
    /// 将 Protobuf Value 转换为 CLR 对象
    /// </summary>
    private static object ConvertFromProtoValue(Value value)
    {
        return value.KindCase switch
        {
            Value.KindOneofCase.NullValue => null!,
            Value.KindOneofCase.NumberValue => value.NumberValue,
            Value.KindOneofCase.StringValue => value.StringValue,
            Value.KindOneofCase.BoolValue => value.BoolValue,
            Value.KindOneofCase.StructValue => ConvertFromProtoStruct(value.StructValue),
            Value.KindOneofCase.ListValue => value.ListValue.Values
                .Select(ConvertFromProtoValue)
                .ToList(),
            _ => value.ToString()
        };
    }

    /// <summary>
    /// 将 Protobuf Struct 转换为字典
    /// </summary>
    private static Dictionary<string, object> ConvertFromProtoStruct(Struct protoStruct)
    {
        var result = new Dictionary<string, object>();

        foreach (var (key, value) in protoStruct.Fields)
        {
            result[key] = ConvertFromProtoValue(value);
        }

        return result;
    }
}