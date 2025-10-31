using Proto;

namespace Aevatar.Agents.ProtoActor;

/// <summary>
/// Proto.Actor 运行时的 Stream 注册表
/// 管理所有 Agent 的 PID 和 Stream
/// </summary>
public class ProtoActorMessageStreamRegistry
{
    private readonly Dictionary<Guid, PID> _pidRegistry = new();
    private readonly Dictionary<Guid, ProtoActorMessageStream> _streamRegistry = new();
    private readonly IRootContext _rootContext;
    private readonly object _lock = new();
    
    public ProtoActorMessageStreamRegistry(IRootContext rootContext)
    {
        _rootContext = rootContext;
    }
    
    /// <summary>
    /// 注册 Agent 的 PID
    /// </summary>
    public void RegisterPid(Guid agentId, PID pid)
    {
        lock (_lock)
        {
            _pidRegistry[agentId] = pid;
            _streamRegistry[agentId] = new ProtoActorMessageStream(agentId, pid, _rootContext);
        }
    }
    
    /// <summary>
    /// 获取 Agent 的 Stream
    /// </summary>
    public ProtoActorMessageStream? GetStream(Guid agentId)
    {
        lock (_lock)
        {
            _streamRegistry.TryGetValue(agentId, out var stream);
            return stream;
        }
    }
    
    /// <summary>
    /// 获取 Agent 的 PID
    /// </summary>
    public PID? GetPid(Guid agentId)
    {
        lock (_lock)
        {
            _pidRegistry.TryGetValue(agentId, out var pid);
            return pid;
        }
    }
    
    /// <summary>
    /// 移除 Agent
    /// </summary>
    public void Remove(Guid agentId)
    {
        lock (_lock)
        {
            _pidRegistry.Remove(agentId);
            _streamRegistry.Remove(agentId);
        }
    }
    
    /// <summary>
    /// 检查是否存在
    /// </summary>
    public bool Exists(Guid agentId)
    {
        lock (_lock)
        {
            return _pidRegistry.ContainsKey(agentId);
        }
    }
}

