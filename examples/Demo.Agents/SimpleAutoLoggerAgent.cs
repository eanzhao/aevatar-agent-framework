using Aevatar.Agents;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Microsoft.Extensions.Logging;

namespace Demo.Agents;

/// <summary>
/// 简单的 Agent 示例 - 演示自动 Logger 注入
/// 不需要在构造函数中处理 Logger
/// </summary>
public class SimpleAutoLoggerAgent : GAgentBase<SimpleAgentState>
{
    private int _processedCount = 0;
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Simple Agent with auto-injected logger. Processed: {_processedCount} events");
    }
    
    [EventHandler]
    public Task HandleWeatherUpdate(WeatherUpdateEvent evt)
    {
        _processedCount++;
        
        // Logger 已经被自动注入，可以直接使用
        Logger.LogInformation("Received weather update: Temp={Temperature}, Condition={Condition}", 
            evt.Temperature, evt.Condition);
        
        State.Counter++;
        State.Attributes["temperature"] = evt.Temperature.ToString();
        State.Attributes["condition"] = evt.Condition;
        
        return Task.CompletedTask;
    }
    
    [EventHandler(Priority = 1)]
    public async Task HandleBroadcast(BroadcastMessage evt)
    {
        _processedCount++;
        
        Logger.LogDebug("Processing broadcast message: {Content} on topic {Topic}", 
            evt.Content, evt.Topic);
        
        State.Items.Add($"Broadcast-{evt.Content}");
        
        // 发布响应事件
        if (EventPublisher != null)
        {
            var response = new RoutingMessage
            {
                Id = Guid.NewGuid().ToString(),
                Content = $"Processed: {evt.Content}",
                RoutingInfo = "processed",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            
            await PublishAsync(response, EventDirection.Up);
            Logger.LogInformation("Published routing message");
        }
    }
    
    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        
        // 初始化状态
        State.Name = $"SimpleAutoLoggerAgent-{Id}";
        State.IsActive = true;
        
        // Logger 在这里已经可用
        Logger.LogInformation("SimpleAutoLoggerAgent {Id} activated", Id);
    }
    
    protected override Task OnDeactivateAsync(CancellationToken ct = default)
    {
        Logger.LogInformation("SimpleAutoLoggerAgent {Id} deactivated after processing {Count} events", 
            Id, _processedCount);
        return base.OnDeactivateAsync(ct);
    }
}
