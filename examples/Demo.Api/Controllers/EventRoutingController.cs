using Microsoft.AspNetCore.Mvc;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Demo.Agents;
using Google.Protobuf.WellKnownTypes;
using Aevatar.Agents.Serialization;

namespace Demo.Api.Controllers;

/// <summary>
/// 展示事件路由功能的控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class EventRoutingController : ControllerBase
{
    private readonly IGAgentActorFactory _agentFactory;
    private readonly ILogger<EventRoutingController> _logger;

    public EventRoutingController(
        IGAgentActorFactory agentFactory,
        ILogger<EventRoutingController> logger)
    {
        _agentFactory = agentFactory;
        _logger = logger;
    }

    /// <summary>
    /// 测试HopCount限制
    /// </summary>
    [HttpPost("hop-count")]
    public async Task<IActionResult> TestHopCount(
        [FromQuery] string runtime = "local",
        [FromQuery] int maxHops = 3,
        [FromQuery] int chainLength = 5)
    {
        try
        {
            // 创建Agent链
            var agents = new List<IGAgentActor>();
            var agentIds = new List<Guid>();
            
            for (int i = 0; i < chainLength; i++)
            {
                var agentId = Guid.NewGuid();
                agentIds.Add(agentId);
                var agent = await _agentFactory.CreateAgentAsync<RouterAgent, RouterState>(agentId);
                agents.Add(agent);
                
                // 建立链式关系
                if (i > 0)
                {
                    await agent.SetParentAsync(agentIds[i - 1]);
                    await agents[i - 1].AddChildAsync(agentId);
                }
            }

            _logger.LogInformation("Created chain of {ChainLength} agents with max hops {MaxHops} on {Runtime}", 
                chainLength, maxHops, runtime);

            // 从第一个Agent发送消息，限制跳数
            var message = new RoutingMessage
            {
                Id = Guid.NewGuid().ToString(),
                Content = $"Test hop count (max: {maxHops})",
                RoutingInfo = $"Chain length: {chainLength}",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var envelope = new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Payload = Any.Pack(message),
                Direction = EventDirection.Down,
                MaxHopCount = maxHops,
                CurrentHopCount = 0
            };

            var eventId = await agents[0].PublishEventAsync(Any.Pack(envelope), EventDirection.Down);
            
            // 等待消息传播
            await Task.Delay(500);

            return Ok(new
            {
                Runtime = runtime,
                EventId = eventId,
                ChainLength = chainLength,
                MaxHops = maxHops,
                ExpectedReach = (int)Math.Min(maxHops + 1, chainLength), // +1 因为包括起始节点
                AgentIds = agentIds,
                Message = message,
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestHopCount");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// 测试事件过滤
    /// </summary>
    [HttpPost("filter")]
    public async Task<IActionResult> TestEventFilter(
        [FromQuery] string runtime = "local",
        [FromQuery] string filterType = "priority")
    {
        try
        {
            // 创建路由器和处理器
            var routerId = Guid.NewGuid();
            var processorId = Guid.NewGuid();
            var filterId = Guid.NewGuid();
            var loggerId = Guid.NewGuid();

            var router = await _agentFactory.CreateAgentAsync<RouterAgent, RouterState>(routerId);
            var processor = await _agentFactory.CreateAgentAsync<ProcessorAgent, ProcessorState>(processorId);
            var filter = await _agentFactory.CreateAgentAsync<FilterAgent, FilterState>(filterId);
            var logger = await _agentFactory.CreateAgentAsync<LoggerAgent, LoggerState>(loggerId);

            // 建立关系：Router -> Filter -> Processor/Logger
            await filter.SetParentAsync(routerId);
            await router.AddChildAsync(filterId);
            
            await processor.SetParentAsync(filterId);
            await filter.AddChildAsync(processorId);
            
            await logger.SetParentAsync(filterId);
            await filter.AddChildAsync(loggerId);

            _logger.LogInformation("Created filtering pipeline on {Runtime}", runtime);

            // 发送不同类型的消息
            var messages = new List<object>();
            
            // 高优先级消息
            var highPriorityMsg = new RoutingMessage
            {
                Id = Guid.NewGuid().ToString(),
                Content = "High priority message",
                RoutingInfo = "priority:high",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            messages.Add(new { Type = "HighPriority", Message = highPriorityMsg });

            // 低优先级消息
            var lowPriorityMsg = new RoutingMessage
            {
                Id = Guid.NewGuid().ToString(),
                Content = "Low priority message",
                RoutingInfo = "priority:low",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            messages.Add(new { Type = "LowPriority", Message = lowPriorityMsg });

            // 发送消息
            foreach (var msg in new[] { highPriorityMsg, lowPriorityMsg })
            {
                var envelope = new EventEnvelope
                {
                    Id = Guid.NewGuid().ToString(),
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Payload = Any.Pack(msg),
                    Direction = EventDirection.Down
                };
                await router.PublishEventAsync(Any.Pack(envelope), EventDirection.Down);
                await Task.Delay(100);
            }

            // 等待处理完成
            await Task.Delay(500);

            return Ok(new
            {
                Runtime = runtime,
                FilterType = filterType,
                Pipeline = new
                {
                    Router = routerId,
                    Filter = filterId,
                    Processor = processorId,
                    Logger = loggerId
                },
                Messages = messages,
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestEventFilter");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// 测试广播功能
    /// </summary>
    [HttpPost("broadcast")]
    public async Task<IActionResult> TestBroadcast(
        [FromQuery] string runtime = "local",
        [FromQuery] int receiverCount = 5)
    {
        try
        {
            // 创建广播Agent
            var broadcasterId = Guid.NewGuid();
            var broadcaster = await _agentFactory.CreateAgentAsync<BroadcastAgent, BroadcastState>(broadcasterId);
            
            // 创建接收者
            var receivers = new List<IGAgentActor>();
            var receiverIds = new List<Guid>();
            
            for (int i = 0; i < receiverCount; i++)
            {
                var receiverId = Guid.NewGuid();
                receiverIds.Add(receiverId);
                var receiver = await _agentFactory.CreateAgentAsync<ProcessorAgent, ProcessorState>(receiverId);
                receivers.Add(receiver);
                
                // 设置广播者为父级
                await receiver.SetParentAsync(broadcasterId);
                await broadcaster.AddChildAsync(receiverId);
            }

            _logger.LogInformation("Created broadcaster with {ReceiverCount} receivers on {Runtime}", 
                receiverCount, runtime);

            // 发送广播消息
            var broadcastMsg = new BroadcastMessage
            {
                Id = Guid.NewGuid().ToString(),
                Topic = "announcement",
                Content = "This is a broadcast message to all receivers",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var envelope = new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Payload = Any.Pack(broadcastMsg),
                Direction = EventDirection.Down // 向所有子节点广播
            };

            var eventId = await broadcaster.PublishEventAsync(Any.Pack(envelope), EventDirection.Down);
            
            // 等待广播完成
            await Task.Delay(500);

            return Ok(new
            {
                Runtime = runtime,
                EventId = eventId,
                BroadcasterId = broadcasterId,
                ReceiverIds = receiverIds,
                ReceiverCount = receiverCount,
                Message = broadcastMsg,
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestBroadcast");
            return StatusCode(500, new { Error = ex.Message });
        }
    }
}

// 消息类型已在 demo_messages.proto 中定义
