using Aevatar.Agents.Persistence.Supabase.Options;
using Aevatar.Agents.Persistence.Supabase.Stores;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Aevatar.Agents.Persistence.Supabase.DependencyInjection;

/// <summary>
/// Supabase(Postgres) persistence DI extensions.
///
/// Design:
/// - Manage connection pool via <see cref="NpgsqlDataSource"/> (recommended)
/// - Auto-trigger table/index creation/permission tightening on store construction (idempotent)
/// </summary>
public static class SupabaseServiceCollectionExtensions
{
    /// <summary>
    /// Register Supabase(Postgres) infrastructure (Options + NpgsqlDataSource).
    /// </summary>
    public static IServiceCollection AddAevatarSupabase(
        this IServiceCollection services,
        string connectionString,
        Action<SupabasePersistenceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionString);

        services
            .AddOptions<SupabasePersistenceOptions>()
            .Configure(o =>
            {
                o.ConnectionString = connectionString;
                configure?.Invoke(o);
            });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SupabasePersistenceOptions>>().Value;
            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                throw new InvalidOperationException(
                    $"{nameof(SupabasePersistenceOptions)}.{nameof(SupabasePersistenceOptions.ConnectionString)} is required.");
            }

            // NpgsqlDataSource.Create does not connect immediately, real connection triggered on first OpenConnection.
            return NpgsqlDataSource.Create(options.ConnectionString);
        });

        return services;
    }

    /// <summary>
    /// Register Supabase(Postgres) infrastructure (Options + NpgsqlDataSource).
    /// Suitable for binding from IConfiguration then fine-tuning.
    /// </summary>
    public static IServiceCollection AddAevatarSupabase(
        this IServiceCollection services,
        Action<SupabasePersistenceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services
            .AddOptions<SupabasePersistenceOptions>()
            .Configure(configure);

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SupabasePersistenceOptions>>().Value;
            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                throw new InvalidOperationException(
                    $"{nameof(SupabasePersistenceOptions)}.{nameof(SupabasePersistenceOptions.ConnectionString)} is required.");
            }

            return NpgsqlDataSource.Create(options.ConnectionString);
        });

        return services;
    }

    /// <summary>
    /// Register Supabase StateStore (specific TState).
    /// Note: In the framework, the more common approach is:
    /// options.StateStoreType = typeof(SupabaseStateStore&lt;&gt;)
    /// </summary>
    public static IServiceCollection AddSupabaseStateStore<TState>(this IServiceCollection services)
        where TState : class, IMessage<TState>, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<SupabaseStateStore<TState>>();
        return services;
    }

    /// <summary>
    /// Register Supabase ConfigStore (specific TConfig).
    /// </summary>
    public static IServiceCollection AddSupabaseConfigStore<TConfig>(this IServiceCollection services)
        where TConfig : class, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<SupabaseConfigStore<TConfig>>();
        return services;
    }

    /// <summary>
    /// Register Supabase EventRouterStore.
    /// </summary>
    public static IServiceCollection AddSupabaseEventRouterStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<SupabaseEventRouterStore>();
        return services;
    }

}


