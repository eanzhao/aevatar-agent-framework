using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.Cognitive.Messages;
using Aevatar.Agents.Cognitive.Primitives;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using System.Text.Json;

using StepDefinition = Aevatar.Agents.Cognitive.Primitives.StepDefinition;

namespace Aevatar.Agents.Cognitive.Agents;

// ============================================================
//  CognitiveCoordinatorGAgent - LLM execution (Coordinator-side)
//
//  WHY:
//  - 这段代码天然会膨胀（streaming + guardrails + UI events）。
//  - 独立成文件，避免污染核心编排逻辑。
// ============================================================

public partial class CognitiveCoordinatorGAgent
{
    // ============================================================
    //  简单步骤 - Coordinator 直接执行
    // ============================================================

    private async Task<PrimitiveResult> ExecuteLlmCallDirectAsync(
        StepDefinition step,
        string? preRenderedPrompt = null,
        string? preRenderedSystem = null)
    {
        // 使用预渲染的 prompt（如果提供），否则现场渲染
        var prompt = preRenderedPrompt ?? _templateEngine.Render(
            step.Parameters.GetValueOrDefault("prompt")?.ToString() ?? "",
            _workflowVariables);

        var systemPrompt = preRenderedSystem;
        if (systemPrompt == null && step.Parameters.TryGetValue("system", out var sysObj) && sysObj != null)
        {
            systemPrompt = _templateEngine.Render(sysObj.ToString()!, _workflowVariables);
        }

        // Unify all Coordinator-side LLM calls with the same reliability guardrails used by vote proposals:
        // - max_length / timeout_seconds / idle_timeout_seconds
        // - streaming hang protection
        // - strict parsing for json/json_array (red-flag on parse null)
        return await ExecuteLlmCallWithStreamingAsync(step, step, systemPrompt, prompt);
    }

    /// <summary>
    /// 执行 LLM 调用，streaming 事件发送到指定的步骤
    /// 用于 vote 并行生成时，每个提案有独立的事件流
    /// </summary>
    private async Task<PrimitiveResult> ExecuteLlmCallWithStreamingAsync(
        StepDefinition generator,
        StepDefinition eventStep,
        string? systemPrompt,
        string userPrompt)
    {
        // Keep State.History bounded before adding new step messages (best-effort).
        await CompactChatHistoryIfNeededAsync();

        // ============================================================
        //  可靠性护栏：
        //  - vote 会并行发起多个 LLM 调用
        //  - 任一调用卡住不返回，会让 vote 卡死在 Task.WhenAll
        //  - 这里统一做：超时 + 异常收敛（超时/异常→返回失败 PrimitiveResult）
        // ============================================================

        Logger.LogInformation("[LLM] ▶ ExecuteLlmCallWithStreamingAsync ENTER for {StepId} (promptLen={Len})",
            eventStep.Id, userPrompt.Length);

        var outputType = generator.Parameters.GetValueOrDefault("output")?.ToString() ?? "text";

        // Guardrails (configurable via DSL / workflow defaults)
        var maxLength = ResolveIntParameter(generator.Parameters, "max_length", 102400);
        maxLength = Math.Clamp(maxLength, 1024, 1024 * 1024); // [1KB, 1MB]

        var timeoutSeconds = ResolveIntParameter(generator.Parameters, "timeout_seconds", 360);
        timeoutSeconds = Math.Clamp(timeoutSeconds, 5, 3600); // [5s, 1h]

        var idleTimeoutSeconds = ResolveIntParameter(generator.Parameters, "idle_timeout_seconds", 30);
        idleTimeoutSeconds = Math.Clamp(idleTimeoutSeconds, 1, timeoutSeconds);

        var strictParse = ResolveBoolParameter(generator.Parameters, "strict_parse", true);

        // NOTE: 不绑定外部 CancellationToken（当前 Coordinator 执行链路未贯通），至少保证不会无限挂死
        var callTimeout = TimeSpan.FromSeconds(timeoutSeconds);
        var idleTimeout = TimeSpan.FromSeconds(idleTimeoutSeconds);
        using var timeoutCts = new CancellationTokenSource(callTimeout);
        var ct = timeoutCts.Token;

        var request = new AevatarLLMRequest
        {
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt
        };

        // Optional: persist step-level transcript to State.History (default off).
        // NOTE:
        // - vote 会在同一个 Coordinator 里并行启动多个 LLM 调用
        // - 我们必须写入 step 元数据，否则并发写入会打乱顺序，UI 无法按 step 归并
        if (EnableChatHistoryInState)
        {
            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                AppendStepHistoryMessage(AevatarChatRole.System, systemPrompt!, eventStep, tokenUsed: 0);
            }
            AppendStepHistoryMessage(AevatarChatRole.User, userPrompt, eventStep, tokenUsed: 0);
        }

        var output = string.Empty;
        var promptTokens = 0;
        var completionTokens = 0;

        try
        {
            Logger.LogInformation("[LLM] {StepId}: Getting model info...", eventStep.Id);
            var modelInfo = await LLMProvider.GetModelInfoAsync(ct);
            var supportsStreaming = modelInfo.SupportsStreaming;
            Logger.LogInformation("[LLM] {StepId}: supportsStreaming={Streaming}", eventStep.Id, supportsStreaming);

            if (supportsStreaming)
            {
                var sb = new System.Text.StringBuilder();
                var tokenIndex = 0;
                var startAt = DateTimeOffset.UtcNow;

                // Streaming events throttling (reduce event storm & threadpool starvation)
                const int StreamPublishEveryN = 16;
                var streamPublishMinInterval = TimeSpan.FromMilliseconds(250);
                var lastPublishAt = DateTimeOffset.MinValue;

                Logger.LogInformation("[STREAM] Starting streaming for {StepId}, Type={Type}",
                    eventStep.Id, eventStep.Type);

                var stream = LLMProvider.GenerateStreamAsync(request, ct);
                var enumerator = stream.GetAsyncEnumerator();
                try
                {
                    while (true)
                    {
                        var elapsed = DateTimeOffset.UtcNow - startAt;
                        var remaining = callTimeout - elapsed;
                        if (remaining <= TimeSpan.Zero)
                        {
                            return new PrimitiveResult
                            {
                                Success = false,
                                Error = $"llm-timeout>{timeoutSeconds}s",
                                SystemPrompt = systemPrompt,
                                UserPrompt = userPrompt,
                                AssistantResponse = output,
                                TokensUsed = 0,
                                LlmCalls = 1
                            };
                        }

                        var moveNextTask = enumerator.MoveNextAsync().AsTask();
                        var waitTimeout = remaining < idleTimeout ? remaining : idleTimeout;
                        var completed = await Task.WhenAny(moveNextTask, Task.Delay(waitTimeout));
                        if (completed != moveNextTask)
                        {
                            // No tokens for idleTimeout => treat as hang (or total timeout if remaining < idleTimeout).
                            return new PrimitiveResult
                            {
                                Success = false,
                                Error = waitTimeout == idleTimeout
                                    ? $"llm-idle-timeout>{idleTimeoutSeconds}s"
                                    : $"llm-timeout>{timeoutSeconds}s",
                                SystemPrompt = systemPrompt,
                                UserPrompt = userPrompt,
                                AssistantResponse = output,
                                TokensUsed = 0,
                                LlmCalls = 1
                            };
                        }

                        if (!await moveNextTask)
                        {
                            completionTokens = tokenIndex;
                            break;
                        }

                        var token = enumerator.Current;
                        var content = token.Content ?? string.Empty;
                        if (string.IsNullOrEmpty(content) && !token.IsComplete)
                            continue;

                        sb.Append(content);
                        output = sb.ToString();

                        // 防止输出爆炸导致内存/渲染/日志被打穿（这类“卡住”看起来像死循环）
                        if (output.Length > maxLength)
                        {
                            return PrimitiveResult.Fail($"redflag-length>{maxLength}");
                        }

                        // 流式事件发送到指定的步骤（每个提案独立显示）
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
                            EmitStepEvent(eventStep, StepStatus.Running,
                                $"Streaming... ({tokenIndex} tokens)",
                                progress: 0,
                                systemPrompt: systemPrompt,
                                userPrompt: userPrompt,
                                assistantResponse: output);
                        }

                        tokenIndex++;

                        // Safety stop for pathological streams
                        if (tokenIndex > 20000)
                        {
                            return PrimitiveResult.Fail("llm-stream-too-long>20000");
                        }

                        if (token.IsComplete)
                        {
                            completionTokens = tokenIndex;
                            Logger.LogInformation("[STREAM] Completed {StepId}: {Tokens} tokens",
                                eventStep.Id, tokenIndex);
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

                // 某些 provider 不会显式发 IsComplete=true（枚举自然结束）
                if (completionTokens == 0 && !string.IsNullOrEmpty(output))
                {
                    completionTokens = Math.Max(1, output.Length / 4);
                }
            }
            else
            {
                AI.Abstractions.AevatarLLMResponse response;
                try
                {
                    response = await LLMProvider.GenerateAsync(request, ct).WaitAsync(callTimeout);
                }
                catch (TimeoutException)
                {
                    return new PrimitiveResult
                    {
                        Success = false,
                        Error = $"llm-timeout>{timeoutSeconds}s",
                        SystemPrompt = systemPrompt,
                        UserPrompt = userPrompt,
                        AssistantResponse = output,
                        TokensUsed = 0,
                        LlmCalls = 1
                    };
                }
                output = response.Content ?? string.Empty;
                promptTokens = response.Usage?.PromptTokens ?? 0;
                completionTokens = response.Usage?.CompletionTokens ?? 0;
            }

            lock (_statsLock)
            {
                CustomState.TotalTokensUsed += promptTokens + completionTokens;
                CustomState.TotalLlmCalls++;
            }

            if (output.Length > maxLength)
            {
                return PrimitiveResult.Fail($"redflag-length>{maxLength}");
            }

            var parser = _parserFactory.Create(outputType);
            var parsed = parser.Parse(output);
            if (parsed == null)
            {
                if (!strictParse)
                {
                    parsed = output;
                }
                else
                {
                    return PrimitiveResult.Fail("redflag-parse-null");
                }
            }

            if (EnableChatHistoryInState && !string.IsNullOrEmpty(output))
            {
                AppendStepHistoryMessage(
                    AevatarChatRole.Assistant,
                    output,
                    eventStep,
                    tokenUsed: promptTokens + completionTokens);
            }

            // Persist a structured per-step interaction into AIMemory (optional).
            // This is the "long-term" layer; State.History stays as a short-term window.
            await PersistInteractionToMemoryAsync(
                eventStep,
                systemPrompt,
                userPrompt,
                output,
                promptTokens,
                completionTokens);

            // Keep State.History bounded after appending (best-effort).
            await CompactChatHistoryIfNeededAsync();

            return new PrimitiveResult
            {
                Success = true,
                Value = parsed,
                TokensUsed = promptTokens + completionTokens,
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                LlmCalls = 1,
                SystemPrompt = systemPrompt,
                UserPrompt = userPrompt,
                AssistantResponse = output
            };
        }
        catch (OperationCanceledException)
        {
            Logger.LogWarning("[LLM] ✗ Timeout/cancel: {StepId} after {Timeout} (promptLen={Len}, outLen={OutLen})",
                eventStep.Id, callTimeout, userPrompt.Length, output.Length);

            return new PrimitiveResult
            {
                Success = false,
                Error = $"llm-timeout>{(int)callTimeout.TotalSeconds}s",
                SystemPrompt = systemPrompt,
                UserPrompt = userPrompt,
                AssistantResponse = output,
                TokensUsed = 0,
                LlmCalls = 1
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LLM] ✗ Error in {StepId}: {Message}", eventStep.Id, ex.Message);

            return new PrimitiveResult
            {
                Success = false,
                Error = $"llm-error:{ex.Message}",
                SystemPrompt = systemPrompt,
                UserPrompt = userPrompt,
                AssistantResponse = output,
                TokensUsed = 0,
                LlmCalls = 1
            };
        }
    }

    private void AppendStepHistoryMessage(
        AevatarChatRole role,
        string content,
        StepDefinition step,
        int tokenUsed)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        var msg = new AevatarChatMessage
        {
            Role = role,
            Content = content,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            TokenUsed = tokenUsed
        };

        msg.Metadata["step_id"] = step.Id ?? string.Empty;
        msg.Metadata["step_type"] = step.Type ?? string.Empty;
        msg.Metadata["execution_id"] = CustomState.ExecutionId ?? string.Empty;
        msg.Metadata["agent_kind"] = "cognitive_coordinator";

        AddMessageToHistory(msg);
    }

    private async Task PersistInteractionToMemoryAsync(
        StepDefinition step,
        string? systemPrompt,
        string userPrompt,
        string assistantResponse,
        int promptTokens,
        int completionTokens)
    {
        if (AIMemory == null)
            return;

        try
        {
            var payload = new
            {
                kind = "aevatar.cognitive.llm_interaction.v1",
                agentId = Id.ToString("N"),
                agentKind = "cognitive_coordinator",
                executionId = CustomState.ExecutionId ?? "",
                stepId = step.Id ?? "",
                stepType = step.Type ?? "",
                systemPrompt,
                userPrompt,
                assistantResponse,
                promptTokens,
                completionTokens,
                totalTokens = promptTokens + completionTokens,
                timestamp = DateTimeOffset.UtcNow.ToString("O")
            };

            var json = JsonSerializer.Serialize(payload);
            await AIMemory.AddMessageAsync("assistant", json);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to persist llm interaction to AIMemory (best-effort).");
        }
    }
}

