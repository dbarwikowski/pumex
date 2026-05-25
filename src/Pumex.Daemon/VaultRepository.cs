using Pumex.Contracts;

namespace Pumex.Daemon;

public class VaultRepository(IndexDbContext context, INoteRepository notes) : IVaultRepository
{
    public async Task<long> AddVaultAsync(string name, string path)
    {
        path = Path.TrimEndingDirectorySeparator(path);
        using var _ = await context.AcquireAsync();
        using var cmd = context.Command("""
            INSERT INTO vaults (name, path) VALUES (@name, @path)
            ON CONFLICT(path) DO UPDATE SET name = excluded.name
            RETURNING id
            """,
            ("@name", name), ("@path", path));
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task<List<VaultRecord>> GetVaultsAsync()
    {
        using var _ = await context.AcquireAsync();
        var result = new List<VaultRecord>();
        using var cmd = context.Command("SELECT id, name, path FROM vaults");
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(new VaultRecord(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
        return result;
    }

    public async Task<VaultRecord?> GetVaultByPathAsync(string path)
    {
        path = Path.TrimEndingDirectorySeparator(path);
        using var _ = await context.AcquireAsync();
        // rtrim handles rows stored before the trailing-separator normalisation was added
        using var cmd = context.Command(
            "SELECT id, name, path FROM vaults WHERE rtrim(path, '/\\') = @path",
            ("@path", path));
        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new VaultRecord(reader.GetInt64(0), reader.GetString(1), reader.GetString(2));
    }

    public async Task<VaultRecord?> GetVaultByNameAsync(string name)
    {
        using var _ = await context.AcquireAsync();
        using var cmd = context.Command(
            "SELECT id, name, path FROM vaults WHERE name = @name",
            ("@name", name));
        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new VaultRecord(reader.GetInt64(0), reader.GetString(1), reader.GetString(2));
    }

    public async Task RemoveVaultAsync(long vaultId)
    {
        // Query paths and IDs to evict from the note cache, then delete.
        // Gate is held only for the DB work; cache eviction uses its own gate.
        List<(long Id, string Path)> toEvict;
        {
            using var _ = await context.AcquireAsync();
            toEvict = new List<(long, string)>();
            using (var q = context.Command(
                "SELECT id, path FROM notes WHERE vault_id = @vaultId",
                ("@vaultId", vaultId)))
            {
                using var reader = await q.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    toEvict.Add((reader.GetInt64(0), reader.GetString(1)));
            }
            // FK ON DELETE CASCADE on notes(vault_id) takes the rest with it (tags,
            // properties, links). FTS rowids are purged by the
            // notes_fts_purge_after_delete trigger as the cascade fires.
            await context.ExecuteAsync("DELETE FROM vaults WHERE id = @id", ("@id", vaultId));
        }

        // Evict stale path↔id cache entries after the transaction is committed.
        if (toEvict.Count > 0)
            await notes.EvictAsync(
                toEvict.Select(x => x.Path).ToList(),
                toEvict.Select(x => x.Id).ToList());
    }

    public async Task<List<TagCount>> GetTagsAsync(long? vaultId = null)
    {
        using var _ = await context.AcquireAsync();
        var result = new List<TagCount>();
        using var cmd = vaultId is null
            ? context.Command("SELECT tag, COUNT(*) FROM tags GROUP BY tag ORDER BY tag")
            : context.Command("""
                SELECT t.tag, COUNT(*)
                FROM tags t
                JOIN notes n ON n.id = t.note_id
                WHERE n.vault_id = @vaultId
                GROUP BY t.tag
                ORDER BY t.tag
                """, ("@vaultId", vaultId.Value));
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(new TagCount(reader.GetString(0), reader.GetInt32(1)));
        return result;
    }
}
