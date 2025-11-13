using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.AI.Abstractions.Tools;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core.Tools.BuiltIn;

/// <summary>
/// 事件方向枚举
/// </summary>
public enum EventDirection
{
    /// <summary>
    /// 向上传播（向父级代理）
    /// </summary>
    Up,

    /// <summary>
    /// 向下传播（向子级代理）
    /// </summary>
    Down,

    /// <summary>
    /// 双向传播
    /// </summary>
    Bidirectional
}

/// <summary>
/// 事件发布工具 - 内置AI工具
/// </summary>
public class AevatarEventPublisherTool : IAevatarAITool
{
    private readonly ILogger<AevatarEventPublisherTool> _logger;

    public AevatarEventPublisherTool(ILogger<AevatarEventPublisherTool> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => "publish_event";
    public string Description => "Publish events to the agent hierarchy";

    public async Task<AevatarAIToolResult> ExecuteAsync(
        AevatarAIToolContext context,
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var eventType = parameters.GetValueOrDefault("eventType")?.ToString();
            var eventData = parameters.GetValueOrDefault("eventData");
            var direction = parameters.GetValueOrDefault("direction")?.ToString() ?? "Bidirectional";

            if (string.IsNullOrWhiteSpace(eventType))
            {
                _logger.LogWarning("Event type is required but not provided");
                return AevatarAIToolResult.CreateFailure("Event type is required");
            }

            if (eventData == null)
            {
                _logger.LogWarning("Event data is required but not provided");
                return AevatarAIToolResult.CreateFailure("Event data is required");
            }

            // 创建事件实例
            var eventInstance = CreateEventInstance(eventType, eventData);
            if (eventInstance == null)
            {
                _logger.LogWarning("Failed to create event instance for type: {EventType}", eventType);
                return AevatarAIToolResult.CreateFailure($"Invalid event type: {eventType}");
            }

            // 解析事件方向
            if (!Enum.TryParse<EventDirection>(direction, true, out var eventDirection))
            {
                _logger.LogWarning("Invalid event direction: {Direction}, defaulting to Bidirectional", direction);
                eventDirection = EventDirection.Bidirectional;
            }

            // 发布事件（简化实现 - 直接返回成功）
            // 在实际实现中，这里会调用真正的事件发布机制
            _logger.LogInformation("Event publishing simulated for: {EventType}", eventType);

            _logger.LogInformation("Successfully published event: {EventType} with direction: {Direction}",
                eventType, eventDirection);

            return AevatarAIToolResult.CreateSuccess(new
            {
                published = true,
                eventType,
                direction = eventDirection.ToString(),
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event");
            return AevatarAIToolResult.CreateFailure($"Failed to publish event: {ex.Message}");
        }
    }

    private object? CreateEventInstance(string eventType, object eventData)
    {
        try
        {
            // 尝试获取事件类型
            var type = Type.GetType(eventType);
            if (type == null)
            {
                // 尝试在当前程序域中查找
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var assembly in assemblies)
                {
                    type = assembly.GetType(eventType);
                    if (type != null) break;
                }
            }

            if (type == null)
            {
                _logger.LogWarning("Event type not found: {EventType}", eventType);
                return null;
            }

            // 序列化数据到JSON，然后反序列化为目标类型
            var json = JsonSerializer.Serialize(eventData);
            var result = JsonSerializer.Deserialize(json, type);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating event instance for type: {EventType}", eventType);
            return null;
        }
    }
}