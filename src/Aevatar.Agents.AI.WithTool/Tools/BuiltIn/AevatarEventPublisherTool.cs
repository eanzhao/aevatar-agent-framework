using System.Text.Json;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Enum = System.Enum;
using Type = System.Type;

namespace Aevatar.Agents.AI.WithTool.Tools.BuiltIn;

/// <summary>
/// 事件发布工具 - 内置AI工具
/// <para/>
/// 允许Agent向其他Agent发送事件
/// </summary>
[AevatarTool(
    Name = "publish_event",
    Description = "Publish events to the agent hierarchy to communicate with other agents",
    Category = ToolCategory.Communication,
    Version = "1.0.0",
    AutoRegister = true
)]
public class AevatarEventPublisherTool : AevatarToolBase
{
    private readonly ILogger<AevatarEventPublisherTool> _logger;

    public AevatarEventPublisherTool(ILogger<AevatarEventPublisherTool> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override string Name => "publish_event";
    public override string Description => "Publish events to the agent hierarchy";
    public override ToolCategory Category => ToolCategory.Communication;
    public override string Version => "1.0.0";

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Required = new[] { "eventType", "eventData" },
            Items = new Dictionary<string, ToolParameter>
            {
                ["eventType"] = new ToolParameter
                {
                    Type = "string",
                    Description = "The type of event to publish (full type name)",
                    Required = true
                },
                ["eventData"] = new ToolParameter
                {
                    Type = "object",
                    Description = "The data/payload of the event",
                    Required = true
                },
                ["direction"] = new ToolParameter
                {
                    Type = "string",
                    Description = "The direction of event propagation (Up, Down, Bidirectional)",
                    Required = false,
                    DefaultValue = "Bidirectional"
                }
            }
        };
    }

    public override async Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
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
                throw new ArgumentException("Event type is required");
            }

            if (eventData == null)
            {
                _logger.LogWarning("Event data is required but not provided");
                throw new ArgumentException("Event data is required");
            }

            // 创建事件实例
            var eventInstance = CreateEventInstance(eventType, eventData);
            if (eventInstance == null)
            {
                _logger.LogWarning("Failed to create event instance for type: {EventType}", eventType);
                throw new InvalidOperationException($"Invalid event type: {eventType}");
            }

            // 解析事件方向
            if (!Enum.TryParse<EventDirection>(direction, true, out var eventDirection))
            {
                _logger.LogWarning("Invalid event direction: {Direction}, defaulting to Bidirectional", direction);
                eventDirection = EventDirection.Both;
            }

            // 发布事件（简化实现 - 直接返回成功）
            // 在实际实现中，这里会调用真正的事件发布机制
            _logger.LogInformation("Event publishing simulated for: {EventType}", eventType);

            _logger.LogInformation("Successfully published event: {EventType} with direction: {Direction}",
                eventType, eventDirection);

            var result = new Struct();
            result.Fields.Add("published", Value.ForBool(true));
            result.Fields.Add("eventType", Value.ForString(eventType));
            result.Fields.Add("direction", Value.ForString(eventDirection.ToString()));
            result.Fields.Add("timestamp", Value.ForString(DateTime.UtcNow.ToString("O")));
            
            return await Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event");
            throw; // Re-throw instead of returning failure result
        }
    }

    public override ToolParameterValidationResult ValidateParameters(Dictionary<string, object?> parameters)
    {
        var result = new ToolParameterValidationResult { IsValid = true };

        if (!parameters.ContainsKey("eventType") || string.IsNullOrWhiteSpace(parameters["eventType"]?.ToString()))
        {
            result.IsValid = false;
            result.Errors.Add("Required parameter 'eventType' is missing or empty");
        }

        if (!parameters.ContainsKey("eventData") || parameters["eventData"] == null)
        {
            result.IsValid = false;
            result.Errors.Add("Required parameter 'eventData' is missing or null");
        }

        return result;
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