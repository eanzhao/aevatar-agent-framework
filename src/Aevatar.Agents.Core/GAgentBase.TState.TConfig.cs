using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.Abstractions.Persistence;
using Google.Protobuf;

namespace Aevatar.Agents.Core;

/// <summary>
/// Agent base class with configuration support
/// Provides separate state and configuration persistence
/// </summary>
/// <typeparam name="TState">Agent state type</typeparam>
/// <typeparam name="TConfig">Agent configuration type</typeparam>
public abstract class GAgentBase<TState, TConfig> : GAgentBase<TState>
    where TState : class, IMessage, new()
    where TConfig : class, IMessage, new()
{
    /// <summary>
    /// Configuration object (writable, automatically persisted to ConfigStore)
    /// </summary>
    protected TConfig Config { get; set; } = new TConfig();

    /// <summary>
    /// Configuration store (injected by Actor layer)
    /// </summary>
    protected IConfigStore<TConfig>? ConfigStore { get; set; }

    /// <summary>
    /// Handle event with automatic configuration and state loading/saving
    /// Extends the parent implementation to add configuration persistence
    /// </summary>
    public override async Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        // 1. Load Configuration (if ConfigStore is configured)
        if (ConfigStore != null)
        {
            Config = await ConfigStore.LoadAsync(Id, ct) ?? new TConfig();
        }

        // 2. Load State (if StateStore is configured)
        if (StateStore != null)
        {
            State = await StateStore.LoadAsync(Id, ct) ?? new TState();
        }

        // 3. Call core event handling implementation
        await HandleEventCoreAsync(envelope, ct);

        // 4. Save Configuration (if ConfigStore is configured)
        if (ConfigStore != null)
        {
            await ConfigStore.SaveAsync(Id, Config, ct);
        }

        // 5. Save State (if StateStore is configured)
        if (StateStore != null)
        {
            await StateStore.SaveAsync(Id, State, ct);
        }
    }

    public async Task ConfigAsync(TConfig config)
    {
        await HandleEventAsync(config.CreateEventEnvelope());
    }

    [EventHandler]
    public async Task HandleConfigAsync(TConfig config)
    {
        Config = config;
    }

    /// <summary>
    /// Get configuration (for agents that need to read config)
    /// </summary>
    public TConfig GetConfig() => Config;
}