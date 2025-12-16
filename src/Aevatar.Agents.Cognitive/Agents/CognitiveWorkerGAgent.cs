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
        // fan_out uses Down broadcast; enforce "exactly one worker handles a subtask"
        // by honoring the reserved variable `__target_worker` (Guid string in "N" format).
        if (request.Variables.TryGetValue("__target_worker", out var targetValue))
        {
            var target = targetValue?.StringValue;
            var self = Id.ToString("N");
            if (!string.IsNullOrWhiteSpace(target) && !string.Equals(target, self, StringComparison.OrdinalIgnoreCase))
            {
                return; // ignore tasks not assigned to me
            }
        }

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
                // IMPORTANT:
                // - For json/json_array outputs, result.Value is a Dictionary/List, and ToString() is useless.
                // - Coordinator needs the RAW assistant response to parse/aggregate deterministically.
                Result = result.AssistantResponse ?? result.Value?.ToString() ?? "",
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

        // Guardrails (configurable via DSL)
        var maxLength = ResolveInt(parameters.GetValueOrDefault("max_length"), 102400);
        maxLength = Math.Clamp(maxLength, 1024, 1024 * 1024); // [1KB, 1MB]

        var timeoutSeconds = ResolveInt(parameters.GetValueOrDefault("timeout_seconds"), 180);
        timeoutSeconds = Math.Clamp(timeoutSeconds, 5, 3600); // [5s, 1h]

        var idleTimeoutSeconds = ResolveInt(parameters.GetValueOrDefault("idle_timeout_seconds"), 30);
        idleTimeoutSeconds = Math.Clamp(idleTimeoutSeconds, 1, timeoutSeconds);

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
            // IMPORTANT:
            // - Provider 的 stream 可能出现两类“假死”：
            //   1) 无视 CancellationToken（CancelAfter 触发也不返回）
            //   2) MoveNextAsync 永远不完成（网络 read 卡住）
            // - 一旦发生，fan_out 会一直等，Pipeline 看起来“前端停了 & 后端也不动”
            // - 解决：不用 await foreach 盲等；改为 MoveNextAsync + WhenAny(Idle/Total timeout)，并对中间态事件做节流。
            var callTimeout = TimeSpan.FromSeconds(timeoutSeconds);
            var idleTimeout = TimeSpan.FromSeconds(idleTimeoutSeconds);
            var startAt = DateTimeOffset.UtcNow;

            // Streaming events throttling (reduce event storm & threadpool starvation)
            const int StreamPublishEveryN = 16;
            var streamPublishMinInterval = TimeSpan.FromMilliseconds(250);
            var lastPublishAt = DateTimeOffset.MinValue;

            try
            {
                var stream = LLMProvider.GenerateStreamAsync(llmRequest, cancellationToken: default);
                var enumerator = stream.GetAsyncEnumerator();
                try
                {
                    while (true)
                    {
                        var elapsed = DateTimeOffset.UtcNow - startAt;
                        var remaining = callTimeout - elapsed;
                        if (remaining <= TimeSpan.Zero)
                            return new PrimitiveResult { Success = false, Error = $"llm-timeout>{(int)callTimeout.TotalSeconds}s" };

                        // Wait for next token, but don't block forever.
                        var moveNextTask = enumerator.MoveNextAsync().AsTask();
                        var waitTimeout = remaining < idleTimeout ? remaining : idleTimeout;
                        var completed = await Task.WhenAny(moveNextTask, Task.Delay(waitTimeout));
                        if (completed != moveNextTask)
                        {
                            // If we haven't received anything for idleTimeout, treat as hang.
                            return new PrimitiveResult
                            {
                                Success = false,
                                Error = waitTimeout == idleTimeout
                                    ? $"llm-idle-timeout>{(int)idleTimeout.TotalSeconds}s"
                                    : $"llm-timeout>{(int)callTimeout.TotalSeconds}s"
                            };
                        }

                        if (!await moveNextTask)
                        {
                            // Stream completed without explicit IsComplete token.
                            totalCompletionTokens = tokenIndex;
                            break;
                        }

                        var token = enumerator.Current;
                        var content = token.Content ?? string.Empty;
                        if (string.IsNullOrEmpty(content) && !token.IsComplete)
                            continue;

                        sb.Append(content);
                        finalContent = sb.ToString();

                        // Streaming report (Success=false indicates intermediate state)
                        // Throttle: publish first/last OR every N "tokens" OR time-based heartbeat.
                        var now = DateTimeOffset.UtcNow;
                        var isFirst = tokenIndex == 0;
                        var shouldPublish =
                            isFirst ||
                            token.IsComplete ||
                            (tokenIndex % StreamPublishEveryN == 0) ||
                            (now - lastPublishAt >= streamPublishMinInterval);

                        if (shouldPublish)
                        {
                            lastPublishAt = now;

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
                        }

                        tokenIndex++;

                        // Safety stop for pathological streams
                        if (tokenIndex > 20000)
                        {
                            return new PrimitiveResult { Success = false, Error = "llm-stream-too-long>20000" };
                        }

                        if (token.IsComplete)
                        {
                            totalCompletionTokens = tokenIndex;
                            break;
                        }
                    }
                }
                finally
                {
                    // Don't allow DisposeAsync to block forever if provider is misbehaving.
                    try
                    {
                        var disposeTask = enumerator.DisposeAsync().AsTask();
                        await Task.WhenAny(disposeTask, Task.Delay(1000));
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return new PrimitiveResult { Success = false, Error = $"llm-timeout>{(int)callTimeout.TotalSeconds}s" };
            }

            // Red-flag：长度
            if (finalContent.Length > maxLength)
            {
                return new PrimitiveResult { Success = false, Error = $"redflag-length>{maxLength}" };
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
            AevatarLLMResponse response;
            try
            {
                response = await LLMProvider.GenerateAsync(llmRequest).WaitAsync(TimeSpan.FromSeconds(timeoutSeconds));
            }
            catch (TimeoutException)
            {
                return new PrimitiveResult { Success = false, Error = $"llm-timeout>{timeoutSeconds}s" };
            }
            finalContent = response.Content ?? string.Empty;
            totalPromptTokens = response.Usage?.PromptTokens ?? 0;
            totalCompletionTokens = response.Usage?.CompletionTokens ?? 0;

            if (finalContent.Length > maxLength)
            {
                return new PrimitiveResult { Success = false, Error = $"redflag-length>{maxLength}" };
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

    private static int ResolveInt(object? value, int defaultValue)
    {
        if (value == null) return defaultValue;
        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            float f => (int)f,
            decimal m => (int)m,
            string s when int.TryParse(s, out var p) => p,
            _ => defaultValue
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