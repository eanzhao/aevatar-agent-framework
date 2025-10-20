using System;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.GAgents;

/// <summary>
/// LLM代理实现类
/// </summary>
public class LlmGAgent : GAgentBase<LLMAgentState>
{
    public LlmGAgent(IServiceProvider serviceProvider, IGAgentFactory factory, IMessageSerializer serializer)
        : base(serviceProvider, factory, serializer)
    {
    }

    public override async Task RegisterEventHandlersAsync(IMessageStream stream, CancellationToken ct = default)
    {
        // 订阅并处理LLM相关事件
        await stream.SubscribeAsync<LLMEvent>(
            async evt =>
            {
                // 简单实现，实际项目中应该调用LLM服务处理
                evt.Response = $"Response to: {evt.Prompt}";
                await RaiseEventAsync(evt, ct);
            }, ct);

        await stream.SubscribeAsync<GeneralConfigEvent>(
            evt =>
            {
                return RaiseEventAsync(evt, ct);
            }, ct);
    }

    public override async Task ApplyEventAsync(EventEnvelope evt, CancellationToken ct = default)
    {
        if (evt.Payload.Is(LLMEvent.Descriptor))
        {
            var llmEvent = evt.Payload.Unpack<LLMEvent>();
            _state.LlmConfig = llmEvent.Response;
            _state.CurrentVersion = evt.Version;
        }
        else if (evt.Payload.Is(GeneralConfigEvent.Descriptor))
        {
            var configEvent = evt.Payload.Unpack<GeneralConfigEvent>();
            _state.LlmConfig = configEvent.ConfigValue;
            _state.CurrentVersion = evt.Version;
        }
        else if (evt.Payload.Is(SubAgentAdded.Descriptor))
        {
            var added = evt.Payload.Unpack<SubAgentAdded>();
            _state.SubAgentIds.Add(added.SubAgentId);
            _state.CurrentVersion = evt.Version;
        }
        else if (evt.Payload.Is(SubAgentRemoved.Descriptor))
        {
            var removed = evt.Payload.Unpack<SubAgentRemoved>();
            _state.SubAgentIds.Remove(removed.SubAgentId);
            _state.CurrentVersion = evt.Version;
        }
    }
}