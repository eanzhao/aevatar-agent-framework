using Google.Protobuf;

namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Agent Actor runtime abstraction interface.
/// Responsibilities: Hierarchy management, Stream subscription, Event routing, Lifecycle management.
/// </summary>
public interface IGAgentActor : IEventPublisher
{
    /// <summary>
    /// Actor Identifier（等同于关联的 <see cref="IGAgent.Id"/>，**统一格式**）。
    ///
    /// 规范：<c>"AgentTypeShortName:RawId"</c>
    /// 例如：<c>"ChatAgent:12345678-..."</c>
    ///
    /// NOTE:
    /// - 创建时允许传入 RawId（通常是 Guid string），系统会自动补齐前缀
    /// - 后续所有 Manager/Stream/Hierarchy 操作都应使用该完整格式
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Get the associated Agent instance.
    /// Note: In Orleans mode, Agent runs in Silo, so this may throw NotSupportedException.
    /// Use GetDescriptionAsync() for safe remote access.
    /// </summary>
    IGAgent GetAgent();

    /// <summary>
    /// Get Agent description (safe for remote access).
    /// </summary>
    Task<string> GetDescriptionAsync();

    // ============ Hierarchy Inspection ============

    /// <summary>
    /// Get all child Agent IDs. Hierarchy mutations should be performed via ActorHierarchyCoordinator
    /// or IGAgentActorManager.LinkParentChildAsync to keep parent/child routers in sync.
    /// </summary>
    Task<IReadOnlyList<string>> GetChildrenAsync();

    /// <summary>
    /// Get the parent Agent ID.
    /// </summary>
    Task<string?> GetParentAsync();

    // ============ Event Publishing and Routing ============

    /// <summary>
    /// Handle received events.
    /// </summary>
    /// <param name="envelope">Event envelope.</param>
    /// <param name="ct">Cancellation token.</param>
    Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default);

    // ============ Lifecycle ============

    /// <summary>
    /// Activate the Actor.
    /// </summary>
    Task ActivateAsync(CancellationToken ct = default);

    /// <summary>
    /// Deactivate the Actor.
    /// </summary>
    Task DeactivateAsync(CancellationToken ct = default);

    /// <summary>
    /// Invoke RPC method on Agent via Protobuf.
    /// For Local runtime, this may directly call the method.
    /// For Orleans runtime, this calls the Grain RPC method.
    /// </summary>
    /// <param name="requestBytes">RpcRequest serialized bytes</param>
    /// <returns>RpcResponse serialized bytes</returns>
    Task<byte[]> InvokeRpcAsync(byte[] requestBytes);
}