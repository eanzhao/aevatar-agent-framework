using System;
using Orleans.Runtime;
using Orleans.Streams;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Orleans Message Stream Provider
/// 为每个 Agent 创建和管理 Orleans Stream
/// </summary>
public class OrleansMessageStreamProvider
{
    private readonly IStreamProvider _streamProvider;
    private readonly string _streamNamespace;
    
    public OrleansMessageStreamProvider(IStreamProvider streamProvider, string streamNamespace = AevatarAgentsOrleansConstants.StreamNamespace)
    {
        _streamProvider = streamProvider;
        _streamNamespace = streamNamespace;
    }
    
    /// <summary>
    /// 获取 Agent 的 Stream
    /// </summary>
    public OrleansMessageStream GetStream(Guid agentId)
    {
        var streamId = StreamId.Create(_streamNamespace, agentId.ToString());
        var stream = _streamProvider.GetStream<byte[]>(streamId);
        return new OrleansMessageStream(agentId, stream);
    }
}

