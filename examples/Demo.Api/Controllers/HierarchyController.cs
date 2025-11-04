using Microsoft.AspNetCore.Mvc;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Demo.Agents;
using Google.Protobuf.WellKnownTypes;

namespace Demo.Api.Controllers;

/// <summary>
/// 展示Agent层级管理功能的控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class HierarchyController : ControllerBase
{
    private readonly IGAgentActorFactory _agentFactory;
    private readonly ILogger<HierarchyController> _logger;

    public HierarchyController(
        IGAgentActorFactory agentFactory,
        ILogger<HierarchyController> logger)
    {
        _agentFactory = agentFactory;
        _logger = logger;
    }

    /// <summary>
    /// 创建组织结构
    /// </summary>
    [HttpPost("organization")]
    public async Task<IActionResult> CreateOrganization(
        [FromQuery] string runtime = "local",
        [FromQuery] int departmentCount = 3,
        [FromQuery] int employeesPerDept = 2)
    {
        try
        {
            // 创建CEO
            var ceoId = Guid.NewGuid();
            var ceo = await _agentFactory.CreateGAgentActorAsync<ManagerAgent>(ceoId);
            
            _logger.LogInformation("Created CEO {CeoId} on {Runtime}", ceoId, runtime);

            var organization = new
            {
                CEO = ceoId,
                Departments = new List<object>()
            };

            // 创建部门
            for (int d = 0; d < departmentCount; d++)
            {
                var managerId = Guid.NewGuid();
                var manager = await _agentFactory.CreateGAgentActorAsync<ManagerAgent>(managerId);
                
                // 设置CEO为父级
                await manager.SetParentAsync(ceoId);
                await ceo.AddChildAsync(managerId);

                var department = new
                {
                    ManagerId = managerId,
                    Employees = new List<Guid>()
                };

                // 创建员工
                for (int e = 0; e < employeesPerDept; e++)
                {
                    var employeeId = Guid.NewGuid();
                    var employee = await _agentFactory.CreateGAgentActorAsync<EmployeeAgent>(employeeId);
                    
                    // 设置经理为父级
                    await employee.SetParentAsync(managerId);
                    await manager.AddChildAsync(employeeId);
                    
                    ((List<Guid>)department.Employees).Add(employeeId);
                }

                ((List<object>)organization.Departments).Add(department);
            }

            return Ok(new
            {
                Runtime = runtime,
                Organization = organization,
                TotalAgents = 1 + departmentCount + (departmentCount * employeesPerDept),
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CreateOrganization");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// 测试事件在层级中的传播
    /// </summary>
    [HttpPost("propagate/{agentId}")]
    public async Task<IActionResult> PropagateEvent(
        Guid agentId,
        [FromQuery] string direction = "down",
        [FromQuery] string runtime = "local")
    {
        try
        {
            // 创建或获取Agent (使用HierarchyAgent作为默认类型)
            var agent = await _agentFactory.CreateGAgentActorAsync<HierarchyAgent>(agentId);

            _logger.LogInformation("Propagating event from {AgentId} {Direction} on {Runtime}", 
                agentId, direction, runtime);

            // 创建消息
            var message = new HierarchyMessage
            {
                Id = Guid.NewGuid().ToString(),
                Content = $"Message from {agentId}",
                Direction = direction,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            // 确定传播方向
            EventDirection eventDirection = direction.ToLower() switch
            {
                "up" => EventDirection.Up,
                "down" => EventDirection.Down,
                "both" => EventDirection.Both,
                _ => EventDirection.Down
            };

            var envelope = new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Payload = Any.Pack(message),
                Direction = eventDirection,
                MaxHopCount = 5 // 限制传播深度
            };

            var eventId = await agent.PublishEventAsync(Any.Pack(envelope), eventDirection);
            
            // 等待消息传播
            await Task.Delay(500);

            return Ok(new
            {
                AgentId = agentId,
                Runtime = runtime,
                EventId = eventId,
                Direction = direction,
                Message = message,
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in PropagateEvent");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// 重组层级结构
    /// </summary>
    [HttpPost("restructure")]
    public async Task<IActionResult> RestructureHierarchy(
        [FromBody] RestructureRequest request,
        [FromQuery] string runtime = "local")
    {
        try
        {
            // 创建或获取涉及的Agent (使用HierarchyAgent作为默认类型)
            var child = await _agentFactory.CreateGAgentActorAsync<HierarchyAgent>(request.ChildId);
            var oldParent = request.OldParentId.HasValue ? 
                await _agentFactory.CreateGAgentActorAsync<HierarchyAgent>(request.OldParentId.Value) : null;
            var newParent = await _agentFactory.CreateGAgentActorAsync<HierarchyAgent>(request.NewParentId);

            if (child == null || newParent == null)
            {
                return BadRequest(new { Error = "Invalid agent IDs" });
            }

            _logger.LogInformation("Restructuring: Moving {ChildId} from {OldParentId} to {NewParentId} on {Runtime}", 
                request.ChildId, request.OldParentId, request.NewParentId, runtime);

            // 从旧父级移除
            if (oldParent != null)
            {
                await oldParent.RemoveChildAsync(request.ChildId);
            }

            // 设置新父级
            await child.SetParentAsync(request.NewParentId);
            await newParent.AddChildAsync(request.ChildId);

            return Ok(new
            {
                Runtime = runtime,
                ChildId = request.ChildId,
                OldParentId = request.OldParentId,
                NewParentId = request.NewParentId,
                Status = "Restructure completed",
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in RestructureHierarchy");
            return StatusCode(500, new { Error = ex.Message });
        }
    }
}

// 请求定义
public class RestructureRequest
{
    public Guid ChildId { get; set; }
    public Guid? OldParentId { get; set; }
    public Guid NewParentId { get; set; }
}

// 消息类型已在 demo_messages.proto 中定义
