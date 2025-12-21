using Aevatar.Agents.Abstractions.EventRouting;
using Aevatar.Agents.Persistence.Supabase.Internal;
using Aevatar.Agents.Persistence.Supabase.Options;
using Aevatar.Agents.Persistence.Supabase.Setup;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Aevatar.Agents.Persistence.Supabase.Stores;

/// <summary>
/// Supabase(Postgres) EventRouter hierarchy store：
/// - parent_id：uuid?
/// - children_ids：uuid[]
/// </summary>
public sealed class SupabaseEventRouterStore : IEventRouterStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly SupabasePersistenceOptions _options;
    private readonly string _table;

    public SupabaseEventRouterStore(NpgsqlDataSource dataSource, IOptions<SupabasePersistenceOptions> options)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        SupabaseSchemaManager.EnsureInitialized(_dataSource, _options);

        _table = SupabaseSql.Table(_options.Schema, _options.EventRouterHierarchiesTable, nameof(_options.EventRouterHierarchiesTable));
    }

    public async Task<EventRouterHierarchy?> LoadAsync(Guid agentId, CancellationToken ct = default)
    {
        var sql = $"SELECT parent_id, children_ids FROM {_table} WHERE agent_id = @agent_id LIMIT 1";

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("agent_id", agentId);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        Guid? parentId = reader.IsDBNull(0) ? null : reader.GetFieldValue<Guid>(0);
        var children = reader.IsDBNull(1) ? Array.Empty<Guid>() : reader.GetFieldValue<Guid[]>(1);

        return new EventRouterHierarchy
        {
            ParentId = parentId,
            ChildrenIds = children.Length == 0 ? new HashSet<Guid>() : new HashSet<Guid>(children)
        };
    }

    public async Task SaveAsync(Guid agentId, EventRouterHierarchy hierarchy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);

        var childrenArray = hierarchy.ChildrenIds?.ToArray() ?? Array.Empty<Guid>();

        var sql = $@"
INSERT INTO {_table} (agent_id, parent_id, children_ids, updated_at)
VALUES (@agent_id, @parent_id, @children_ids, now())
ON CONFLICT (agent_id)
DO UPDATE SET
  parent_id = EXCLUDED.parent_id,
  children_ids = EXCLUDED.children_ids,
  updated_at = EXCLUDED.updated_at";

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("agent_id", agentId);
        cmd.Parameters.AddWithValue("parent_id", (object?)hierarchy.ParentId ?? DBNull.Value);

        var p = cmd.Parameters.AddWithValue("children_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, childrenArray);
        p.Value = childrenArray;

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid agentId, CancellationToken ct = default)
    {
        var sql = $"DELETE FROM {_table} WHERE agent_id = @agent_id";

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("agent_id", agentId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> ExistsAsync(Guid agentId, CancellationToken ct = default)
    {
        var sql = $"SELECT 1 FROM {_table} WHERE agent_id = @agent_id LIMIT 1";

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("agent_id", agentId);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }
}

/// <summary>
/// Supabase EventRouter store factory（用于手工 DI/高级场景）。
/// </summary>
public static class SupabaseEventRouterStoreFactory
{
    public static Func<IServiceProvider, IEventRouterStore> Create()
    {
        return sp =>
        {
            var dataSource = sp.GetService(typeof(NpgsqlDataSource)) as NpgsqlDataSource
                ?? throw new InvalidOperationException(
                    "NpgsqlDataSource not registered. Call services.AddAevatarSupabase(...) first.");

            var options = sp.GetService(typeof(IOptions<SupabasePersistenceOptions>)) as IOptions<SupabasePersistenceOptions>
                ?? throw new InvalidOperationException(
                    "SupabasePersistenceOptions not registered. Call services.AddAevatarSupabase(...) first.");

            return new SupabaseEventRouterStore(dataSource, options);
        };
    }
}


