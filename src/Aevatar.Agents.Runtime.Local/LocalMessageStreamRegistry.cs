namespace Aevatar.Agents.Runtime.Local;

/// <summary>
/// Local 运行时的 Stream 注册表
/// 管理所有 Agent 的 Message Stream
/// </summary>
public class LocalMessageStreamRegistry
{
    private readonly Dictionary<Guid, LocalMessageStream> _streams = new();
    private readonly Lock _lock = new();

    /// <summary>
    /// 获取或创建 Agent 的 Stream
    /// </summary>
    public LocalMessageStream GetOrCreateStream(Guid agentId, int capacity = 1000)
    {
        lock (_lock)
        {
            if (!_streams.TryGetValue(agentId, out var stream))
            {
                stream = new LocalMessageStream(agentId, capacity);
                _streams[agentId] = stream;
            }

            return stream;
        }
    }

    /// <summary>
    /// 检查 Stream 是否已存在
    /// </summary>
    public bool StreamExists(Guid agentId)
    {
        lock (_lock)
        {
            return _streams.ContainsKey(agentId);
        }
    }

    /// <summary>
    /// 移除 Agent 的 Stream
    /// </summary>
    public void RemoveStream(Guid agentId)
    {
        lock (_lock)
        {
            if (_streams.TryGetValue(agentId, out var stream))
            {
                stream.Stop();
                _streams.Remove(agentId);
            }
        }
    }

    /// <summary>
    /// 获取 Stream（如果存在）
    /// </summary>
    public LocalMessageStream? GetStream(Guid agentId)
    {
        lock (_lock)
        {
            _streams.TryGetValue(agentId, out var stream);
            return stream;
        }
    }
}