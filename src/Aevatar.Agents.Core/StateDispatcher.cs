using System.Threading.Channels;
using Aevatar.Agents.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Agents.Core;

/// <summary>
/// 状态分发器实现
/// 使用 Channel 进行异步状态分发
/// </summary>
public class StateDispatcher : IStateDispatcher
{
    private readonly ILogger<StateDispatcher> _logger;

    // 单个状态变更的 Channel（实时发布）
    private readonly Dictionary<Guid, Channel<object>> _singleChannels = new();

    // 批量状态变更的 Channel（批处理）
    private readonly Dictionary<Guid, Channel<object>> _batchChannels = new();

    private readonly object _lock = new();

    public StateDispatcher(ILogger<StateDispatcher>? logger = null)
    {
        _logger = logger ?? NullLogger<StateDispatcher>.Instance;
    }

    public async Task PublishSingleAsync<TState>(
        Guid agentId,
        StateSnapshot<TState> snapshot,
        CancellationToken ct = default)
        where TState : class, new()
    {
        var channel = GetOrCreateSingleChannel(agentId);

        _logger.LogDebug("Publishing single state change for agent {AgentId}, version {Version}",
            agentId, snapshot.Version);

        await channel.Writer.WriteAsync(snapshot, ct);
    }

    public async Task PublishBatchAsync<TState>(
        Guid agentId,
        StateSnapshot<TState> snapshot,
        CancellationToken ct = default)
        where TState : class, new()
    {
        var channel = GetOrCreateBatchChannel(agentId);

        _logger.LogDebug("Publishing batch state change for agent {AgentId}, version {Version}",
            agentId, snapshot.Version);

        await channel.Writer.WriteAsync(snapshot, ct);
    }

    public Task SubscribeAsync<TState>(
        Guid agentId,
        Func<StateSnapshot<TState>, Task> handler,
        CancellationToken ct = default)
        where TState : class, new()
    {
        var channel = GetOrCreateSingleChannel(agentId);

        _logger.LogInformation("Subscribing to state changes for agent {AgentId}", agentId);

        // 启动订阅处理循环
        _ = Task.Run(async () =>
        {
            await foreach (var item in channel.Reader.ReadAllAsync(ct))
            {
                if (item is StateSnapshot<TState> snapshot)
                {
                    try
                    {
                        await handler(snapshot);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in state subscription handler for agent {AgentId}",
                            agentId);
                    }
                }
            }
        }, ct);

        return Task.CompletedTask;
    }

    private Channel<object> GetOrCreateSingleChannel(Guid agentId)
    {
        lock (_lock)
        {
            if (!_singleChannels.TryGetValue(agentId, out var channel))
            {
                channel = Channel.CreateBounded<object>(new BoundedChannelOptions(100)
                {
                    FullMode = BoundedChannelFullMode.DropOldest
                });
                _singleChannels[agentId] = channel;
            }

            return channel;
        }
    }

    private Channel<object> GetOrCreateBatchChannel(Guid agentId)
    {
        lock (_lock)
        {
            if (!_batchChannels.TryGetValue(agentId, out var channel))
            {
                channel = Channel.CreateBounded<object>(new BoundedChannelOptions(1000)
                {
                    FullMode = BoundedChannelFullMode.DropOldest
                });
                _batchChannels[agentId] = channel;
            }

            return channel;
        }
    }
}
