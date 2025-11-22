using System;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.AI.WithTool.Messages;

/// <summary>
/// Partial class extensions for AevatarAIToolContext protobuf message
/// Runtime fields that can't be serialized are handled here
/// </summary>
public partial class AevatarAIToolContext
{
    // Runtime-only fields (not serialized)
    private IServiceProvider? _serviceProvider;
    private CancellationToken _cancellationToken;

    /// <summary>
    /// Service provider (runtime only, not serialized)
    /// </summary>
    public IServiceProvider ServiceProvider
    {
        get => _serviceProvider ?? throw new InvalidOperationException("ServiceProvider not initialized");
        set => _serviceProvider = value;
    }

    /// <summary>
    /// Cancellation token (runtime only, not serialized)
    /// </summary>
    public CancellationToken CancellationToken
    {
        get => _cancellationToken;
        set => _cancellationToken = value;
    }

    /// <summary>
    /// Creates a new context with runtime dependencies
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="serviceProvider">Service provider</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A new context instance</returns>
    public static AevatarAIToolContext Create(
        string agentId,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        return new AevatarAIToolContext
        {
            AgentId = agentId,
            ServiceProvider = serviceProvider,
            CancellationToken = cancellationToken
        };
    }

    /// <summary>
    /// Gets a required service from the service provider
    /// </summary>
    /// <typeparam name="T">Service type</typeparam>
    /// <returns>The service instance</returns>
    public T GetService<T>() where T : notnull
    {
        return ServiceProvider.GetRequiredService<T>();
    }

    /// <summary>
    /// Gets an optional service from the service provider
    /// </summary>
    /// <typeparam name="T">Service type</typeparam>
    /// <returns>The service instance or null</returns>
    public T? GetOptionalService<T>() where T : class
    {
        return ServiceProvider.GetService<T>();
    }

    /// <summary>
    /// Gets configuration from the service provider
    /// </summary>
    /// <typeparam name="T">Configuration type</typeparam>
    /// <returns>Configuration instance</returns>
    public T GetConfiguration<T>() where T : class, new()
    {
        var configuration = GetOptionalService<IConfiguration>();
        if (configuration != null)
        {
            var config = new T();
            configuration.Bind(typeof(T).Name, config);
            return config;
        }
        return new T();
    }

    /// <summary>
    /// Adds or updates metadata
    /// </summary>
    /// <param name="key">Metadata key</param>
    /// <param name="value">Metadata value</param>
    public void AddMetadata(string key, string value)
    {
        Metadata[key] = value;
    }
}
