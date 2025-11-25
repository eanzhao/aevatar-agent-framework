using System;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Hierarchy;
using Aevatar.App.Agents.Agents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp.AspNetCore.Mvc;

namespace Aevatar.App.Controllers;

/// <summary>
/// Agent Demo Controller
/// Demonstrates Aevatar Agent Framework integration
/// Supports both Local and Orleans runtimes
/// </summary>
[Route("api/agent-demo")]
[ApiController]
public class AgentDemoController : AbpControllerBase
{
    private readonly IGAgentActorManager _actorManager;
    private readonly ILogger<AgentDemoController> _logger;

    public AgentDemoController(
        IGAgentActorManager actorManager,
        ILogger<AgentDemoController> logger)
    {
        _actorManager = actorManager;
        _logger = logger;
    }

    /// <summary>
    /// Create a new agent
    /// </summary>
    /// <returns>Agent creation response with ID and description</returns>
    [HttpPost("agents")]
    public async Task<ActionResult<AgentCreatedResponse>> CreateAgent()
    {
        var agentId = Guid.NewGuid();
        
        _logger.LogInformation("🚀 Creating agent with ID: {AgentId}", agentId);

        try
        {
            // Create and register agent actor (managed lifecycle)
            var actor = await _actorManager.CreateAndRegisterAsync<SimpleBusinessAgent>(agentId);

            // Get description from the agent
            var agent = actor.GetAgent();
            var description = await agent.GetDescriptionAsync();

            _logger.LogInformation("✅ Agent {AgentId} created successfully", agentId);

            return Ok(new AgentCreatedResponse
            {
                AgentId = agentId.ToString(),
                Description = description,
                CreatedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error creating agent {AgentId}", agentId);
            return StatusCode(500, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Send message to agent
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="request">Message request</param>
    /// <returns>Agent response</returns>
    [HttpPost("agents/{agentId}/messages")]
    public async Task<ActionResult<AgentMessageResponse>> SendMessage(
        [FromRoute] string agentId,
        [FromBody] AgentMessageRequest request)
    {
        if (!Guid.TryParse(agentId, out var id))
        {
            return BadRequest("Invalid agent ID format");
        }

        _logger.LogInformation("📨 Sending message to agent {AgentId}: {Message}", 
            agentId, request.Message);

        try
        {
            // Get existing agent or create if not exists
            var actor = await _actorManager.GetActorAsync(id);
            if (actor == null)
            {
                actor = await _actorManager.CreateAndRegisterAsync<SimpleBusinessAgent>(id);
            }

            // Cast to specific agent type to call business methods
            var agent = actor.GetAgent() as SimpleBusinessAgent;
            if (agent == null)
            {
                return StatusCode(500, "Agent type mismatch");
            }

            // Process message
            var result = await agent.ProcessMessageAsync(request.Message);

            _logger.LogInformation("✅ Message processed successfully");

            return Ok(new AgentMessageResponse
            {
                AgentId = agentId,
                Response = result,
                ProcessedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing message for agent {AgentId}", agentId);
            return StatusCode(500, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get agent statistics
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <returns>Agent statistics</returns>
    [HttpGet("agents/{agentId}/stats")]
    public async Task<ActionResult<AgentStatsResponse>> GetStatistics([FromRoute] string agentId)
    {
        if (!Guid.TryParse(agentId, out var id))
        {
            return BadRequest("Invalid agent ID format");
        }

        _logger.LogInformation("📊 Getting stats for agent {AgentId}", agentId);

        try
        {
            // Get existing agent
            var actor = await _actorManager.GetActorAsync(id);
            if (actor == null)
            {
                return NotFound($"Agent {agentId} not found");
            }

            var agent = actor.GetAgent() as SimpleBusinessAgent;
            if (agent == null)
            {
                return StatusCode(500, "Agent type mismatch");
            }

            var stats = await agent.GetStatisticsAsync();

            return Ok(new AgentStatsResponse
            {
                AgentId = stats.AgentId,
                ProcessedEventsCount = stats.ProcessedCount,
                LastMessage = stats.LastMessage,
                LastUpdated = stats.LastUpdated?.ToDateTime() ?? DateTime.MinValue
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error getting stats for agent {AgentId}", agentId);
            return StatusCode(500, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Set parent for agent (establishes hierarchy)
    /// </summary>
    [HttpPost("agents/{childId}/parent/{parentId}")]
    public async Task<IActionResult> SetParent([FromRoute] string childId, [FromRoute] string parentId)
    {
        if (!Guid.TryParse(childId, out var cId) || !Guid.TryParse(parentId, out var pId))
        {
            return BadRequest("Invalid ID format");
        }

        try
        {
            var childActor = await _actorManager.GetActorAsync(cId);
            if (childActor == null) return NotFound($"Child agent {childId} not found");

            var parentActor = await _actorManager.GetActorAsync(pId);
            if (parentActor == null) return NotFound($"Parent agent {parentId} not found");

            // Establish bidirectional relationship using ActorHierarchyCoordinator
            await ActorHierarchyCoordinator.LinkAsync(parentActor, childActor, _logger);
            
            _logger.LogInformation("✅ Parent-child relationship established: {Child} -> {Parent}", childId, parentId);
            return Ok(new { message = $"Child {childId} now has parent {parentId}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error setting parent");
            return StatusCode(500, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Publish event to agent's stream
    /// </summary>
    [HttpPost("agents/{agentId}/events")]
    public async Task<IActionResult> PublishEvent(
        [FromRoute] string agentId,
        [FromBody] AgentEventRequest request)
    {
        if (!Guid.TryParse(agentId, out var id))
        {
            return BadRequest("Invalid agent ID format");
        }

        try
        {
            var actor = await _actorManager.GetActorAsync(id);
            if (actor == null) return NotFound($"Agent {agentId} not found");

            var agent = actor.GetAgent() as SimpleBusinessAgent;
            if (agent == null) return StatusCode(500, "Agent type mismatch");

            // Create event and publish
            var evt = new Business.Server.BusinessMessageEvent
            {
                Message = request.Message,
                Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            };

            await agent.ProcessMessageAsync(request.Message);
            
            _logger.LogInformation("✅ Event published to agent {AgentId}", agentId);
            return Ok(new { message = "Event published successfully", eventData = request.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error publishing event");
            return StatusCode(500, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Test agent health
    /// </summary>
    [HttpGet("health")]
    public IActionResult GetHealth()
    {
        return Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow });
    }
}

// ========== DTOs ==========

public class AgentCreatedResponse
{
    public string AgentId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class AgentMessageRequest
{
    public string Message { get; set; } = string.Empty;
}

public class AgentMessageResponse
{
    public string AgentId { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; }
}

public class AgentStatsResponse
{
    public string AgentId { get; set; } = string.Empty;
    public int ProcessedEventsCount { get; set; }
    public string LastMessage { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; }
}

public class AgentEventRequest
{
    public string Message { get; set; } = string.Empty;
}

