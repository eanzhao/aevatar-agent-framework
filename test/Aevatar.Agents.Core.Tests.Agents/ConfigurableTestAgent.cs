using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Abstractions.Persistence;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core.Tests.Agents;

/// <summary>
/// 测试Agent - 带状态和配置
/// </summary>
public class ConfigurableTestAgent : GAgentBase<TestAgentState, TestAgentConfig>
{
    public override string GetDescription()
    {
        return $"ConfigurableAgent: {Config.AgentName} (Retries: {Config.MaxRetries})";
    }

    protected override Task OnActivateAsync(CancellationToken ct = default)
    {
        // 初始化配置
        Config.AgentName = "ConfigurableAgent";
        Config.MaxRetries = 3;
        Config.TimeoutSeconds = 30;
        Config.EnableLogging = true;

        // 初始化状态
        State.Name = Config.AgentName;
        State.Counter = 0;

        return base.OnActivateAsync(ct);
    }

    [EventHandler]
    public async Task HandleTestEventAsync(TestEvent evt)
    {
        if (Config.EnableLogging)
        {
            Logger.LogInformation("Handling event: {EventId}", evt.EventId);
        }

        State.Counter++;

        // 使用配置中的重试逻辑
        for (var i = 0; i < Config.MaxRetries; i++)
        {
            try
            {
                // 模拟可能失败的操作
                await ProcessEventWithRetry(evt);
                break;
            }
            catch when (i < Config.MaxRetries - 1)
            {
                await Task.Delay(100);
            }
        }
    }

    [EventHandler]
    public async Task ChangeStateAsync(Empty empty)
    {
        State.Name = "DualGeneric";
        State.Counter = 100;
    }

    private Task ProcessEventWithRetry(TestEvent evt)
    {
        // 模拟处理逻辑
        return Task.CompletedTask;
    }
}