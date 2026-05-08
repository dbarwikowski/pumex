using Microsoft.Data.Sqlite;
using Pumex.Contracts;

namespace Pumex.Daemon;

public class IndexDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public IndexDb(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        ApplyPragmas();
        EnsureSchema();
    }

    private void ApplyPragmas()
    {
        Execute("PRAGMA journal_mode=WAL");
        Execute("PRAGMA synchronous=NORMAL");
        Execute("PRAGMA foreign_keys=ON");
    }

    private void EnsureSchema()
    {
        Execute("""
            CREATE TABLE IF NOT EXISTS vaults (
                id   INTEGER PRIMARY KEY,
                name TEXT UNIQUE NOT NULL,
                path TEXT UNIQUE NOT NULL
            );

            CREATE TABLE IF NOT EXISTS notes (
                id    INTEGER PRIMARY KEY,
                vault_id INTEGER REFERENCES vaults(id) ON DELETE CASCADE,
                path  TEXT UNIQUE NOT NULL,
                name  TEXT NOT NULL,
                mtime INTEGER NOT NULL,
                size  INTEGER NOT NULL
            );

            -- Contentless FTS: notes_fts.rowid == notes.id. Body terms are
            -- indexed but the original text is not stored — snippets are
            -- computed lazily from disk.
            CREATE VIRTUAL TABLE IF NOT EXISTS notes_fts USING fts5(
                name,
                body,
                content='',
                tokenize='unicode61'
            );

            CREATE TABLE IF NOT EXISTS properties (
                note_id INTEGER REFERENCES notes(id) ON DELETE CASCADE,
                key     TEXT NOT NULL,
                value   TEXT,
                type    TEXT
            );

            CREATE TABLE IF NOT EXISTS tags (
                note_id INTEGER REFERENCES notes(id) ON DELETE CASCADE,
                tag     TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS links (
                source_id   INTEGER REFERENCES notes(id) ON DELETE CASCADE,
                target_path TEXT NOT NULL,
                resolved_id INTEGER REFERENCES notes(id) ON DELETE SET NULL
            );

            CREATE INDEX IF NOT EXISTS idx_tags_tag      ON tags(tag);
            CREATE INDEX IF NOT EXISTS idx_tags_note     ON tags(note_id);
            CREATE INDEX IF NOT EXISTS idx_links_source  ON links(source_id);
            CREATE INDEX IF NOT EXISTS idx_links_target  ON links(resolved_id);
            CREATE INDEX IF NOT EXISTS idx_links_target_path ON links(target_path);
            CREATE INDEX IF NOT EXISTS idx_notes_mtime   ON notes(mtime);
            CREATE INDEX IF NOT EXISTS idx_notes_vault   ON notes(vault_id);
        """);
    }

    // -------------------------
    // Vaults
    // -------------------------

    public async Task<long> AddVaultAsync(string name, string path)
    {
        using var cmd = Command("""
            INSERT INTO vaults (name, path) VALUES (@name, @path)
            ON CONFLICT(path) DO UPDATE SET name = excluded.name
            RETURNING id
            """,
            ("@name", name), ("@path", path));
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task<List<VaultRecord>> GetVaultsAsync()
    {
        var result = new List<VaultRecord>();
        using var cmd = Command("SELECT id, name, path FROM vaults");
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(new VaultRecord(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
        return result;
    }

    public async Task<VaultRecord?> GetVaultByPathAsync(string path)
    {
        using var cmd = Command("SELECT id, name, path FROM vaults WHERE path = @path", ("@path", path));
        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new VaultRecord(reader.GetInt64(0), reader.GetString(1), reader.GetString(2));
    }

    public async Task<VaultRecord?> GetVaultByNameAsync(string name)
    {
        using var cmd = Command("SELECT id, name, path FROM vaults WHERE name = @name", ("@name", name));
        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new VaultRecord(reader.GetInt64(0), reader.GetString(1), reader.GetString(2));
    }

    // -------------------------
    // Notes
    // -------------------------

    public async Task<Dictionary<string, long>> GetAllMtimesAsync(long vaultId)
    {
        var result = new Dictionary<string, long>();
        using var cmd = Command(
            "SELECT path, mtime FROM notes WHERE vault_id = @vaultId",
            ("@vaultId", vaultId));
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result[reader.GetString(0)] = reader.GetInt64(1);
        return result;
    }

    public async Task UpsertNotesAsync(long vaultId, IEnumerable<NoteDocument> notes)
    {
        using var tx = _connection.BeginTransaction();
        try
        {
            foreach (var note in notes)
            {
                var name = Path.GetFileNameWithoutExtension(note.Path);

                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO notes (vault_id, path, name, mtime, size)
                    VALUES (@vaultId, @path, @name, @mtime, @size)
                    ON CONFLICT(path) DO UPDATE SET
                        name  = excluded.name,
                        mtime = excluded.mtime,
                        size  = excluded.size
                    RETURNING id
                    """;
                cmd.Parameters.AddWithValue("@vaultId", vaultId);
                cmd.Parameters.AddWithValue("@path", note.Path);
                cmd.Parameters.AddWithValue("@name", name);
                cmd.Parameters.AddWithValue("@mtime", note.Mtime);
                cmd.Parameters.AddWithValue("@size", note.Size);

                var noteId = (long)(await cmd.ExecuteScalarAsync())!;

                await DeleteNoteChildrenAsync(noteId, tx);
                await InsertTagsAsync(noteId, note.Tags, tx);
                await InsertPropertiesAsync(noteId, note.Frontmatter, tx);
                await InsertLinksAsync(noteId, note.OutgoingLinks, tx);
                await UpsertFtsAsync(noteId, name, note.Content, tx);
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task DeleteNoteAsync(string path)
    {
        using var tx = _connection.BeginTransaction();
        try
        {
            using var idCmd = _connection.CreateCommand();
            idCmd.Transaction = tx;
            idCmd.CommandText = "SELECT id FROM notes WHERE path = @path";
            idCmd.Parameters.AddWithValue("@path", path);
            var idObj = await idCmd.ExecuteScalarAsync();
            if (idObj is long id)
            {
                using var ftsCmd = _connection.CreateCommand();
                ftsCmd.Transaction = tx;
                ftsCmd.CommandText = "INSERT INTO notes_fts(notes_fts, rowid, name, body) VALUES('delete', @id, '', '')";
                ftsCmd.Parameters.AddWithValue("@id", id);
                await ftsCmd.ExecuteNonQueryAsync();
            }

            using var del = _connection.CreateCommand();
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
    }

    public async Task<List<string>> GetAllPathsAsync(long vaultId)
    {
        var result = new List<string>();
        using var cmd = Command("SELECT path FROM notes WHERE vault_id = @vaultId", ("@vaultId", vaultId));
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0));
        return result;
    }

    // -------------------------
    // Search
    // -------------------------

    public async Task<List<SearchResult>> SearchAsync(string query, int limit = 50, long? vaultId = null)
    {
        // Contentless FTS — get matching note paths, then build snippets from
        // disk in C#. Best-effort substring match against the raw query string;
        // works fine for plain queries, degrades for complex FTS expressions.
        var matches = new List<(string Path, string Name)>();
        var sql = vaultId is null
            ? """
                SELECT n.path, n.name
                FROM notes_fts
                JOIN notes n ON n.id = notes_fts.rowid
                WHERE notes_fts MATCH @query
                ORDER BY rank
                LIMIT @limit
                """
            : """
                SELECT n.path, n.name
                FROM notes_fts
                JOIN notes n ON n.id = notes_fts.rowid
                WHERE notes_fts MATCH @query AND n.vault_id = @vaultId
                ORDER BY rank
                LIMIT @limit
                """;
        var parameters = vaultId is null
            ? new (string, object)[] { ("@query", query), ("@limit", limit) }
            : new (string, object)[] { ("@query", query), ("@limit", limit), ("@vaultId", vaultId.Value) };

        using (var cmd = Command(sql, parameters))
        {
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                matches.Add((reader.GetString(0), reader.GetString(1)));
        }

        return matches
            .Select(m => new SearchResult(m.Path, m.Name, BuildSnippet(m.Path, query)))
            .ToList();
    }

    private static string BuildSnippet(string filePath, string query)
    {
        var needle = StripFtsOperators(query);
        if (string.IsNullOrWhiteSpace(needle)) return "";
        try
        {
            foreach (var line in File.ReadLines(filePath))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                if (trimmed.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    return trimmed.Length > 200 ? trimmed[..200] + "..." : trimmed;
            }
        }
        catch { /* file deleted between match and read — ignore */ }
        return "";
    }

    private static string StripFtsOperators(string query)
    {
        // Pull the first bareword out of an FTS query for the snippet probe.
        var span = query.AsSpan().Trim();
        var start = 0;
        while (start < span.Length && !char.IsLetterOrDigit(span[start])) start++;
        var end = start;
        while (end < span.Length && (char.IsLetterOrDigit(span[end]) || span[end] == '_')) end++;
        return start == end ? "" : span[start..end].ToString();
    }

    // -------------------------
    // Tags
    // -------------------------

    public async Task<List<TagCount>> GetTagsAsync(long? vaultId = null)
    {
        var result = new List<TagCount>();
        using var cmd = vaultId is null
            ? Command("SELECT tag, COUNT(*) FROM tags GROUP BY tag ORDER BY tag")
            : Command("""
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

    // -------------------------
    // Backlinks
    // -------------------------

    public async Task<List<string>> GetBacklinksAsync(string path, long? vaultId = null)
    {
        var result = new List<string>();
        using var cmd = vaultId is null
            ? Command("""
                SELECT DISTINCT n.path
                FROM links l
                JOIN notes n ON n.id = l.source_id
                JOIN notes t ON t.id = l.resolved_id
                WHERE t.path = @path
                """, ("@path", path))
            : Command("""
                SELECT DISTINCT n.path
                FROM links l
                JOIN notes n ON n.id = l.source_id
                JOIN notes t ON t.id = l.resolved_id
                WHERE t.path = @path AND n.vault_id = @vaultId
                """, ("@path", path), ("@vaultId", vaultId.Value));
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0));
        return result;
    }

    // -------------------------
    // Link resolution
    // -------------------------

    public async Task SetLinkResolutionAsync(long sourceId, string targetText, long? resolvedId)
    {
        await ExecuteAsync("""
            UPDATE links SET resolved_id = @resolvedId
            WHERE source_id = @sourceId AND target_path = @targetText
            """,
            ("@resolvedId", (object?)resolvedId ?? DBNull.Value),
            ("@sourceId", sourceId),
            ("@targetText", targetText));
    }

    public async Task<List<UnresolvedLink>> GetUnresolvedLinksAsync(long vaultId)
    {
        var result = new List<UnresolvedLink>();
        using var cmd = Command("""
            SELECT l.source_id, n.path, l.target_path
            FROM links l
            JOIN notes n ON n.id = l.source_id
            WHERE n.vault_id = @vaultId AND l.resolved_id IS NULL
            """, ("@vaultId", vaultId));
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(new UnresolvedLink(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
        return result;
    }

    public async Task<long?> GetNoteIdAsync(string path)
    {
        using var cmd = Command("SELECT id FROM notes WHERE path = @path", ("@path", path));
        var result = await cmd.ExecuteScalarAsync();
        return result is long id ? id : null;
    }

    public async Task<List<string>> GetNotePathsByNameAsync(long vaultId, string name)
    {
        var result = new List<string>();
        using var cmd = Command(
            "SELECT path FROM notes WHERE vault_id = @vaultId AND name = @name COLLATE NOCASE",
            ("@vaultId", vaultId), ("@name", name));
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(reader.GetString(0));
        return result;
    }

    // -------------------------
    // Private helpers
    // -------------------------

    private async Task DeleteNoteChildrenAsync(long noteId, SqliteTransaction tx)
    {
        foreach (var sql in new[]
        {
            "DELETE FROM tags       WHERE note_id = @id",
            "DELETE FROM properties WHERE note_id = @id",
            "DELETE FROM links      WHERE source_id = @id",
        })
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@id", noteId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task InsertTagsAsync(long noteId, List<string> tags, SqliteTransaction tx)
    {
        foreach (var tag in tags)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO tags (note_id, tag) VALUES (@noteId, @tag)";
            cmd.Parameters.AddWithValue("@noteId", noteId);
            cmd.Parameters.AddWithValue("@tag", tag);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task InsertPropertiesAsync(long noteId, Dictionary<string, object> props, SqliteTransaction tx)
    {
        foreach (var (key, value) in props)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO properties (note_id, key, value) VALUES (@noteId, @key, @value)";
            cmd.Parameters.AddWithValue("@noteId", noteId);
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@value", value?.ToString() ?? "");
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task InsertLinksAsync(long noteId, List<string> links, SqliteTransaction tx)
    {
        foreach (var link in links)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO links (source_id, target_path) VALUES (@noteId, @target)";
            cmd.Parameters.AddWithValue("@noteId", noteId);
            cmd.Parameters.AddWithValue("@target", link);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task UpsertFtsAsync(long noteId, string name, string body, SqliteTransaction tx)
    {
        // Contentless FTS doesn't support UPDATE — delete then insert keyed by rowid = notes.id.
        using var del = _connection.CreateCommand();
        del.Transaction = tx;
        del.CommandText = "INSERT INTO notes_fts(notes_fts, rowid, name, body) VALUES('delete', @id, '', '')";
        del.Parameters.AddWithValue("@id", noteId);
        try { await del.ExecuteNonQueryAsync(); }
        catch (SqliteException) { /* row not in FTS yet — first insert */ }

        using var ins = _connection.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = "INSERT INTO notes_fts (rowid, name, body) VALUES (@id, @name, @body)";
        ins.Parameters.AddWithValue("@id", noteId);
        ins.Parameters.AddWithValue("@name", name);
        ins.Parameters.AddWithValue("@body", body);
        await ins.ExecuteNonQueryAsync();
    }

    private void Execute(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        using var cmd = Command(sql, parameters);
        await cmd.ExecuteNonQueryAsync();
    }

    private SqliteCommand Command(string sql, params (string Name, object Value)[] parameters)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        return cmd;
    }

    public void Dispose() => _connection.Dispose();
}

public record UnresolvedLink(long SourceId, string SourcePath, string TargetText);
