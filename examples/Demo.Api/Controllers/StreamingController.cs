using Microsoft.AspNetCore.Mvc;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Demo.Agents;
using Google.Protobuf.WellKnownTypes;

namespace Demo.Api.Controllers;

/// <summary>
/// 展示消息流功能的控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class StreamingController : ControllerBase
{
    private readonly IGAgentActorFactory _agentFactory;
    private readonly ILogger<StreamingController> _logger;

    public StreamingController(
        IGAgentActorFactory agentFactory,
        ILogger<StreamingController> logger)
    {
        _agentFactory = agentFactory;
        _logger = logger;
    }

    /// <summary>
    /// 创建一个流处理Agent并发送消息流
    /// </summary>
    [HttpPost("stream")]
    public async Task<IActionResult> StreamMessages(
        [FromQuery] string runtime = "local",
        [FromQuery] int messageCount = 10)
    {
        try
        {
            // 创建流处理Agent
            var agentId = Guid.NewGuid();
            var agent = await _agentFactory.CreateGAgentActorAsync<StreamProcessorAgent, StreamState>(agentId);
            
            _logger.LogInformation("Created StreamProcessorAgent {AgentId} on {Runtime}", agentId, runtime);

            // 发送消息流
            var results = new List<object>();
            for (int i = 0; i < messageCount; i++)
            {
                var message = new StreamMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    Content = $"Message {i + 1}",
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                var envelope = new EventEnvelope
                {
                    Id = Guid.NewGuid().ToString(),
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Payload = Any.Pack(message),
                    Direction = EventDirection.Down
                };

                var eventId = await agent.PublishEventAsync(Any.Pack(envelope), EventDirection.Down);
                results.Add(new { MessageId = message.Id, EventId = eventId, Index = i + 1 });
                
                // 模拟流延迟
                await Task.Delay(100);
            }

            // 等待处理完成
            await Task.Delay(500);
            
            return Ok(new
            {
                AgentId = agentId,
                Runtime = runtime,
                MessagesSent = messageCount,
                Results = results,
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in StreamMessages");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// 创建发布-订阅模式的流
    /// </summary>
    [HttpPost("pubsub")]
    public async Task<IActionResult> PublishSubscribe(
        [FromQuery] string runtime = "local",
        [FromQuery] int subscriberCount = 3)
    {
        try
        {
            // 创建发布者
            var publisherId = Guid.NewGuid();
            var publisher = await _agentFactory.CreateGAgentActorAsync<PublisherAgent, PublisherState>(publisherId);
            
            // 创建订阅者
            var subscribers = new List<IGAgentActor>();
            var subscriberIds = new List<Guid>();
            
            for (int i = 0; i < subscriberCount; i++)
            {
                var subscriberId = Guid.NewGuid();
                subscriberIds.Add(subscriberId);
                var subscriber = await _agentFactory.CreateGAgentActorAsync<SubscriberAgent, SubscriberState>(subscriberId);
                subscribers.Add(subscriber);
                
                // 建立父子关系（发布者为父）
                await subscriber.SetParentAsync(publisherId);
                await publisher.AddChildAsync(subscriberId);
            }

            _logger.LogInformation("Created Publisher {PublisherId} with {SubscriberCount} subscribers on {Runtime}", 
                publisherId, subscriberCount, runtime);

            // 发布消息
            var publishMessage = new PublishMessage
            {
                Topic = "test-topic",
                Content = "Hello Subscribers!",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var envelope = new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Payload = Any.Pack(publishMessage),
                Direction = EventDirection.Down // 向下传播给所有订阅者
            };

            var eventId = await publisher.PublishEventAsync(Any.Pack(envelope), EventDirection.Down);
            
            // 等待消息传播
            await Task.Delay(500);

            return Ok(new
            {
                PublisherId = publisherId,
                SubscriberIds = subscriberIds,
                Runtime = runtime,
                EventId = eventId,
                Message = publishMessage,
                SubscriberCount = subscriberCount,
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in PublishSubscribe");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// 测试双向流
    /// </summary>
    [HttpPost("bidirectional")]
    public async Task<IActionResult> BidirectionalStream(
        [FromQuery] string runtime = "local")
    {
        try
        {
            // 创建三层Agent结构
            var rootId = Guid.NewGuid();
            var middleId = Guid.NewGuid();
            var leafId = Guid.NewGuid();

            var root = await _agentFactory.CreateGAgentActorAsync<StreamProcessorAgent, StreamState>(rootId);
            var middle = await _agentFactory.CreateGAgentActorAsync<StreamProcessorAgent, StreamState>(middleId);
            var leaf = await _agentFactory.CreateGAgentActorAsync<StreamProcessorAgent, StreamState>(leafId);

            // 建立层级关系
            await middle.SetParentAsync(rootId);
            await root.AddChildAsync(middleId);
            
            await leaf.SetParentAsync(middleId);
            await middle.AddChildAsync(leafId);

            _logger.LogInformation("Created bidirectional stream hierarchy on {Runtime}", runtime);

            // 从叶子节点发送双向消息
            var message = new StreamMessage
            {
                Id = Guid.NewGuid().ToString(),
                Content = "Bidirectional Message",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var envelope = new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Payload = Any.Pack(message),
                Direction = EventDirection.Bidirectional // 双向传播
            };

            var eventId = await leaf.PublishEventAsync(Any.Pack(envelope), EventDirection.Bidirectional);
            
            // 等待消息传播
            await Task.Delay(500);

            return Ok(new
            {
                Runtime = runtime,
                EventId = eventId,
                Hierarchy = new
                {
                    Root = rootId,
                    Middle = middleId,
                    Leaf = leafId
                },
                Message = message,
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in BidirectionalStream");
            return StatusCode(500, new { Error = ex.Message });
        }
    }
}

// 消息类型已在 demo_messages.proto 中定义
