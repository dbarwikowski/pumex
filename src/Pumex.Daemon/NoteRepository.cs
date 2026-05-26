using Microsoft.Data.Sqlite;
using Pumex.Contracts;

namespace Pumex.Daemon;

public class NoteRepository(IndexDbContext context) : INoteRepository
{
    // All access serialised by the gate in IndexDbContext, so plain Dictionary is safe.
    private readonly Dictionary<string, long> _pathToId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, string> _idToPath = new();

    // ── mtimes / paths ──────────────────────────────────────────────────────

    public async Task<Dictionary<string, long>> GetAllMtimesAsync(long vaultId)
    {
        using var _ = await context.AcquireAsync();
        var result = new Dictionary<string, long>();
        using var cmd = context.Command(
            "SELECT path, mtime FROM notes WHERE vault_id = @vaultId",
            ("@vaultId", vaultId));
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result[reader.GetString(0)] = reader.GetInt64(1);
        return result;
    }

    public async Task<List<string>> GetAllPathsAsync(long vaultId)
    {
        using var _ = await context.AcquireAsync();
        var result = new List<string>();
        using var cmd = context.Command(
            "SELECT path FROM notes WHERE vault_id = @vaultId",
            ("@vaultId", vaultId));
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0));
        return result;
    }

    public async Task<List<NoteSummary>> ListNotesAsync(long? vaultId = null)
    {
        using var _ = await context.AcquireAsync();
        var result = new List<NoteSummary>();
        using var cmd = vaultId is null
            ? context.Command("SELECT path, name, mtime, size FROM notes ORDER BY mtime DESC")
            : context.Command(
                "SELECT path, name, mtime, size FROM notes WHERE vault_id = @vaultId ORDER BY mtime DESC",
                ("@vaultId", vaultId.Value));
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(new NoteSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetInt64(3)));
        return result;
    }

    // ── upsert (called under external gate + transaction) ───────────────────

    public async Task<UpsertResult> UpsertCoreAsync(
        SqliteTransaction tx,
        long vaultId,
        IEnumerable<NoteDocument> notes)
    {
        var upserted = new List<(string Path, long Id)>();
        var links    = new List<(long NoteId, string TargetPath)>();

        // Prepare every command once for the transaction and rebind parameter
        // values per-note. Creating fresh SqliteCommands inside the loop was
        // ~10 commands per note × 10k notes = 100k preparations and dominated
        // cold-scan wall time.
        using var upsertCmd = context.Connection.CreateCommand();
        upsertCmd.Transaction = tx;
        upsertCmd.CommandText = """
            INSERT INTO notes (vault_id, path, name, mtime, size)
            VALUES ($vaultId, $path, $name, $mtime, $size)
            ON CONFLICT(path) DO UPDATE SET
                name  = excluded.name,
                mtime = excluded.mtime,
                size  = excluded.size
            RETURNING id
            """;
        var pVaultId = upsertCmd.Parameters.Add("$vaultId", SqliteType.Integer);
        var pPath    = upsertCmd.Parameters.Add("$path",    SqliteType.Text);
        var pName    = upsertCmd.Parameters.Add("$name",    SqliteType.Text);
        var pMtime   = upsertCmd.Parameters.Add("$mtime",   SqliteType.Integer);
        var pSize    = upsertCmd.Parameters.Add("$size",    SqliteType.Integer);
        pVaultId.Value = vaultId;

        using var delTagsCmd  = context.PrepareById(tx, "DELETE FROM tags       WHERE note_id   = $id", out var pDelTagsId);
        using var delPropsCmd = context.PrepareById(tx, "DELETE FROM properties WHERE note_id   = $id", out var pDelPropsId);

        using var insTagCmd = context.Connection.CreateCommand();
        insTagCmd.Transaction = tx;
        insTagCmd.CommandText = "INSERT INTO tags (note_id, tag) VALUES ($noteId, $tag)";
        var pInsTagNoteId = insTagCmd.Parameters.Add("$noteId", SqliteType.Integer);
        var pInsTagTag    = insTagCmd.Parameters.Add("$tag",    SqliteType.Text);

        using var insPropCmd = context.Connection.CreateCommand();
        insPropCmd.Transaction = tx;
        insPropCmd.CommandText = "INSERT INTO properties (note_id, key, value) VALUES ($noteId, $key, $value)";
        var pInsPropNoteId = insPropCmd.Parameters.Add("$noteId", SqliteType.Integer);
        var pInsPropKey    = insPropCmd.Parameters.Add("$key",    SqliteType.Text);
        var pInsPropValue  = insPropCmd.Parameters.Add("$value",  SqliteType.Text);

        using var ftsDelCmd = context.PrepareById(tx, "DELETE FROM notes_fts WHERE rowid = $id", out var pFtsDelId);

        using var ftsInsCmd = context.Connection.CreateCommand();
        ftsInsCmd.Transaction = tx;
        ftsInsCmd.CommandText = "INSERT INTO notes_fts (rowid, name, body) VALUES ($id, $name, $body)";
        var pFtsInsId   = ftsInsCmd.Parameters.Add("$id",   SqliteType.Integer);
        var pFtsInsName = ftsInsCmd.Parameters.Add("$name", SqliteType.Text);
        var pFtsInsBody = ftsInsCmd.Parameters.Add("$body", SqliteType.Text);

        foreach (var note in notes)
        {
            var name = Path.GetFileNameWithoutExtension(note.Path);

            pPath.Value  = note.Path;
            pName.Value  = name;
            pMtime.Value = note.Mtime;
            pSize.Value  = note.Size;
            var noteId = (long)(await upsertCmd.ExecuteScalarAsync())!;
            upserted.Add((note.Path, noteId));

            pDelTagsId.Value  = noteId;
            pDelPropsId.Value = noteId;
            await delTagsCmd.ExecuteNonQueryAsync();
            await delPropsCmd.ExecuteNonQueryAsync();

            pInsTagNoteId.Value = noteId;
            foreach (var tag in note.Tags)
            {
                pInsTagTag.Value = tag;
                await insTagCmd.ExecuteNonQueryAsync();
            }

            pInsPropNoteId.Value = noteId;
            foreach (var (key, value) in note.Frontmatter)
            {
                pInsPropKey.Value   = key;
                pInsPropValue.Value = value switch
                {
                    null => "",
                    System.Collections.IEnumerable list and not string =>
                        string.Join(", ", list.Cast<object>().Select(o => o?.ToString() ?? "")),
                    _ => value.ToString() ?? ""
                };
                await insPropCmd.ExecuteNonQueryAsync();
            }

            // Collect links for the caller (LinkRepository) to write.
            foreach (var link in note.OutgoingLinks)
                links.Add((noteId, link));

            // Contentless FTS doesn't support UPDATE — delete then insert.
            // DELETE on a missing rowid is a no-op (contentless_delete=1).
            pFtsDelId.Value = noteId;
            await ftsDelCmd.ExecuteNonQueryAsync();

            pFtsInsId.Value   = noteId;
            pFtsInsName.Value = name;
            pFtsInsBody.Value = note.Content;
            await ftsInsCmd.ExecuteNonQueryAsync();
        }

        return new UpsertResult(upserted, links);
    }

    // ── delete ──────────────────────────────────────────────────────────────

    public async Task DeleteNoteAsync(string path)
    {
        using var _ = await context.AcquireAsync();
        using var tx = context.BeginTransaction();
        long? deletedId = null;
        try
        {
            using var idCmd = context.Connection.CreateCommand();
            idCmd.Transaction = tx;
            idCmd.CommandText = "SELECT id FROM notes WHERE path = @path";
            idCmd.Parameters.AddWithValue("@path", path);
            var idObj = await idCmd.ExecuteScalarAsync();
            if (idObj is long id) deletedId = id;

            // FTS purge happens via the notes_fts_purge_after_delete trigger.
            using var del = context.Connection.CreateCommand();
            del.Transaction = tx;
            del.CommandText = "DELETE FROM notes WHERE path = @path";
            del.Parameters.AddWithValue("@path", path);
            await del.ExecuteNonQueryAsync();

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
        if (deletedId.HasValue)
        {
            _pathToId.Remove(path);
            _idToPath.Remove(deletedId.Value);
        }
    }

    // ── ID / path lookup ────────────────────────────────────────────────────

    public async Task<long?> GetNoteIdAsync(string path)
    {
        using var _ = await context.AcquireAsync();
        if (_pathToId.TryGetValue(path, out var cached))
            return cached;
        using var cmd = context.Command("SELECT id FROM notes WHERE path = @path", ("@path", path));
        var result = await cmd.ExecuteScalarAsync();
        if (result is long id)
        {
            _pathToId[path] = id;
            _idToPath[id] = path;
            return id;
        }
        return null;
    }

    public async Task<string?> GetNotePathByIdAsync(long id)
    {
        using var _ = await context.AcquireAsync();
        if (_idToPath.TryGetValue(id, out var cached))
            return cached;
        using var cmd = context.Command("SELECT path FROM notes WHERE id = @id", ("@id", id));
        var result = await cmd.ExecuteScalarAsync();
        if (result is string path)
        {
            _idToPath[id] = path;
            _pathToId[path] = id;
            return path;
        }
        return null;
    }

    public async Task<List<string>> GetNotePathsByNameAsync(long vaultId, string name)
    {
        using var _ = await context.AcquireAsync();
        var result = new List<string>();
        using var cmd = context.Command(
            "SELECT path FROM notes WHERE vault_id = @vaultId AND name = @name COLLATE NOCASE",
            ("@vaultId", vaultId), ("@name", name));
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(reader.GetString(0));
        return result;
    }

    // ── properties ──────────────────────────────────────────────────────────

    public async Task<List<PropertyEntry>> GetPropertiesAsync(long noteId)
    {
        using var _ = await context.AcquireAsync();
        var result = new List<PropertyEntry>();
        using var cmd = context.Command(
            "SELECT key, value FROM properties WHERE note_id = @noteId ORDER BY key",
            ("@noteId", noteId));
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(new PropertyEntry(
                reader.GetString(0),
                reader.IsDBNull(1) ? "" : reader.GetString(1)));
        return result;
    }

    public async Task<string?> GetPropertyAsync(long noteId, string key)
    {
        using var _ = await context.AcquireAsync();
        using var cmd = context.Command(
            "SELECT value FROM properties WHERE note_id = @noteId AND key = @key",
            ("@noteId", noteId), ("@key", key));
        var result = await cmd.ExecuteScalarAsync();
        return result is string s ? s : null;
    }

    // ── cache management ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void UpdateCacheUnsafe(IReadOnlyList<(string Path, long Id)> entries)
    {
        foreach (var (path, id) in entries)
        {
            _pathToId[path] = id;
            _idToPath[id] = path;
        }
    }

    /// <inheritdoc/>
    public void EvictUnsafe(IReadOnlyList<string> paths, IReadOnlyList<long> ids)
    {
        foreach (var p in paths) _pathToId.Remove(p);
        foreach (var id in ids) _idToPath.Remove(id);
    }

    /// <inheritdoc/>
    public async Task EvictAsync(IReadOnlyList<string> paths, IReadOnlyList<long> ids)
    {
        using var _ = await context.AcquireAsync();
        EvictUnsafe(paths, ids);
    }
}
