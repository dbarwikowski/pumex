using Microsoft.Data.Sqlite;
using Pumex.Contracts;

namespace Pumex.Daemon;

public class IndexDb : IDisposable
{
    private readonly SqliteConnection _connection;
    // Microsoft.Data.Sqlite doesn't support concurrent transactions on a single
    // connection (BeginTransaction throws if one is already in flight), and
    // we share one IndexDb across every IndexingService. Serialise public
    // methods through this gate. The throughput cost is small — SQLite + WAL
    // is fast — and the per-vault-connection refactor can come later.
    private readonly SemaphoreSlim _gate = new(1, 1);
    // All access is serialised by _gate, so plain Dictionary is safe here.
    private readonly Dictionary<string, long> _pathToId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, string> _idToPath = new();

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

            -- Purge FTS rowids when notes are deleted (explicit DELETE *and*
            -- cascade from vaults). Without this, RemoveVaultAsync left FTS
            -- orphans that grew the index file over time. Empty-string column
            -- values are wrong for a contentless FTS5 table, but searches
            -- JOIN notes_fts with notes(rowid), so any internal inconsistency
            -- is invisible to query results — the rowid is what matters.
            CREATE TRIGGER IF NOT EXISTS notes_fts_purge_after_delete
            AFTER DELETE ON notes BEGIN
                INSERT INTO notes_fts(notes_fts, rowid, name, body)
                VALUES('delete', old.id, '', '');
            END;
        """);
    }

    // -------------------------
    // Vaults
    // -------------------------

    public async Task<long> AddVaultAsync(string name, string path)
    {
        path = Path.TrimEndingDirectorySeparator(path);
        using var _ = await AcquireAsync();
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
        using var _ = await AcquireAsync();
        var result = new List<VaultRecord>();
        using var cmd = Command("SELECT id, name, path FROM vaults");
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(new VaultRecord(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
        return result;
    }

    public async Task<VaultRecord?> GetVaultByPathAsync(string path)
    {
        path = Path.TrimEndingDirectorySeparator(path);
        using var _ = await AcquireAsync();
        // rtrim handles rows stored before the trailing-separator normalisation was added
        using var cmd = Command("SELECT id, name, path FROM vaults WHERE rtrim(path, '/\\') = @path", ("@path", path));
        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new VaultRecord(reader.GetInt64(0), reader.GetString(1), reader.GetString(2));
    }

    public async Task<VaultRecord?> GetVaultByNameAsync(string name)
    {
        using var _ = await AcquireAsync();
        using var cmd = Command("SELECT id, name, path FROM vaults WHERE name = @name", ("@name", name));
        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new VaultRecord(reader.GetInt64(0), reader.GetString(1), reader.GetString(2));
    }

    public async Task RemoveVaultAsync(long vaultId)
    {
        using var _ = await AcquireAsync();
        var toEvict = new List<(long Id, string Path)>();
        using (var qCmd = Command(
            "SELECT id, path FROM notes WHERE vault_id = @vaultId",
            ("@vaultId", vaultId)))
        {
            using var reader = await qCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                toEvict.Add((reader.GetInt64(0), reader.GetString(1)));
        }
        // FK ON DELETE CASCADE on notes(vault_id) takes the rest with it (tags,
        // properties, links). FTS rowids are purged by the
        // notes_fts_purge_after_delete trigger as the cascade fires.
        using var del = Command("DELETE FROM vaults WHERE id = @vaultId", ("@vaultId", vaultId));
        await del.ExecuteNonQueryAsync();
        foreach (var (id, path) in toEvict)
        {
            _pathToId.Remove(path);
            _idToPath.Remove(id);
        }
    }

    // -------------------------
    // Notes
    // -------------------------

    public async Task<Dictionary<string, long>> GetAllMtimesAsync(long vaultId)
    {
        using var _ = await AcquireAsync();
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
        using var _ = await AcquireAsync();
        using var tx = _connection.BeginTransaction();
        var upserted = new List<(string Path, long Id)>();

        // Prepare every command once for the transaction and rebind parameter
        // values per-note. Creating fresh SqliteCommands inside the loop was
        // ~10 commands per note × 10k notes = 100k preparations and dominated
        // cold-scan wall time.
        using var upsertCmd = _connection.CreateCommand();
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
        var pUpVaultId = upsertCmd.Parameters.Add("$vaultId", SqliteType.Integer);
        var pUpPath    = upsertCmd.Parameters.Add("$path",    SqliteType.Text);
        var pUpName    = upsertCmd.Parameters.Add("$name",    SqliteType.Text);
        var pUpMtime   = upsertCmd.Parameters.Add("$mtime",   SqliteType.Integer);
        var pUpSize    = upsertCmd.Parameters.Add("$size",    SqliteType.Integer);
        pUpVaultId.Value = vaultId;

        using var delTagsCmd  = PrepareById(tx, "DELETE FROM tags       WHERE note_id   = $id", out var pDelTagsId);
        using var delPropsCmd = PrepareById(tx, "DELETE FROM properties WHERE note_id   = $id", out var pDelPropsId);
        using var delLinksCmd = PrepareById(tx, "DELETE FROM links      WHERE source_id = $id", out var pDelLinksId);

        using var insTagCmd = _connection.CreateCommand();
        insTagCmd.Transaction = tx;
        insTagCmd.CommandText = "INSERT INTO tags (note_id, tag) VALUES ($noteId, $tag)";
        var pInsTagNoteId = insTagCmd.Parameters.Add("$noteId", SqliteType.Integer);
        var pInsTagTag    = insTagCmd.Parameters.Add("$tag",    SqliteType.Text);

        using var insPropCmd = _connection.CreateCommand();
        insPropCmd.Transaction = tx;
        insPropCmd.CommandText = "INSERT INTO properties (note_id, key, value) VALUES ($noteId, $key, $value)";
        var pInsPropNoteId = insPropCmd.Parameters.Add("$noteId", SqliteType.Integer);
        var pInsPropKey    = insPropCmd.Parameters.Add("$key",    SqliteType.Text);
        var pInsPropValue  = insPropCmd.Parameters.Add("$value",  SqliteType.Text);

        using var insLinkCmd = _connection.CreateCommand();
        insLinkCmd.Transaction = tx;
        insLinkCmd.CommandText = "INSERT INTO links (source_id, target_path) VALUES ($noteId, $target)";
        var pInsLinkNoteId = insLinkCmd.Parameters.Add("$noteId", SqliteType.Integer);
        var pInsLinkTarget = insLinkCmd.Parameters.Add("$target", SqliteType.Text);

        using var ftsDelCmd = PrepareById(tx, "INSERT INTO notes_fts(notes_fts, rowid, name, body) VALUES('delete', $id, '', '')", out var pFtsDelId);

        using var ftsInsCmd = _connection.CreateCommand();
        ftsInsCmd.Transaction = tx;
        ftsInsCmd.CommandText = "INSERT INTO notes_fts (rowid, name, body) VALUES ($id, $name, $body)";
        var pFtsInsId   = ftsInsCmd.Parameters.Add("$id",   SqliteType.Integer);
        var pFtsInsName = ftsInsCmd.Parameters.Add("$name", SqliteType.Text);
        var pFtsInsBody = ftsInsCmd.Parameters.Add("$body", SqliteType.Text);

        try
        {
            foreach (var note in notes)
            {
                var name = Path.GetFileNameWithoutExtension(note.Path);

                pUpPath.Value  = note.Path;
                pUpName.Value  = name;
                pUpMtime.Value = note.Mtime;
                pUpSize.Value  = note.Size;
                var noteId = (long)(await upsertCmd.ExecuteScalarAsync())!;
                upserted.Add((note.Path, noteId));

                pDelTagsId.Value  = noteId;
                pDelPropsId.Value = noteId;
                pDelLinksId.Value = noteId;
                await delTagsCmd.ExecuteNonQueryAsync();
                await delPropsCmd.ExecuteNonQueryAsync();
                await delLinksCmd.ExecuteNonQueryAsync();

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
                    pInsPropValue.Value = value?.ToString() ?? "";
                    await insPropCmd.ExecuteNonQueryAsync();
                }

                pInsLinkNoteId.Value = noteId;
                foreach (var link in note.OutgoingLinks)
                {
                    pInsLinkTarget.Value = link;
                    await insLinkCmd.ExecuteNonQueryAsync();
                }

                // Contentless FTS doesn't support UPDATE — delete then insert.
                pFtsDelId.Value = noteId;
                try { await ftsDelCmd.ExecuteNonQueryAsync(); }
                catch (SqliteException) { /* row not in FTS yet — first insert */ }

                pFtsInsId.Value   = noteId;
                pFtsInsName.Value = name;
                pFtsInsBody.Value = note.Content;
                await ftsInsCmd.ExecuteNonQueryAsync();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
        foreach (var (path, id) in upserted)
        {
            _pathToId[path] = id;
            _idToPath[id] = path;
        }
    }

    private SqliteCommand PrepareById(SqliteTransaction tx, string sql, out SqliteParameter idParam)
    {
        var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        idParam = cmd.Parameters.Add("$id", SqliteType.Integer);
        return cmd;
    }

    public async Task DeleteNoteAsync(string path)
    {
        using var _ = await AcquireAsync();
        using var tx = _connection.BeginTransaction();
        long? deletedId = null;
        try
        {
            using var idCmd = _connection.CreateCommand();
            idCmd.Transaction = tx;
            idCmd.CommandText = "SELECT id FROM notes WHERE path = @path";
            idCmd.Parameters.AddWithValue("@path", path);
            var idObj = await idCmd.ExecuteScalarAsync();
            if (idObj is long id) deletedId = id;

            // FTS purge happens via the notes_fts_purge_after_delete trigger.
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
        if (deletedId.HasValue)
        {
            _pathToId.Remove(path);
            _idToPath.Remove(deletedId.Value);
        }
    }

    public async Task<List<string>> GetAllPathsAsync(long vaultId)
    {
        using var _ = await AcquireAsync();
        var result = new List<string>();
        using var cmd = Command("SELECT path FROM notes WHERE vault_id = @vaultId", ("@vaultId", vaultId));
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0));
        return result;
    }

    public async Task<List<NoteSummary>> ListNotesAsync(long? vaultId = null)
    {
        using var _ = await AcquireAsync();
        var result = new List<NoteSummary>();
        using var cmd = vaultId is null
            ? Command("SELECT path, name, mtime, size FROM notes ORDER BY mtime DESC")
            : Command(
                "SELECT path, name, mtime, size FROM notes WHERE vault_id = @vaultId ORDER BY mtime DESC",
                ("@vaultId", vaultId.Value));
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(new NoteSummary(reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3)));
        return result;
    }

    // -------------------------
    // Search
    // -------------------------

    public async Task<List<SearchResult>> SearchAsync(
        string? query,
        int limit = 50,
        long? vaultId = null,
        IReadOnlyList<string>? tags = null,
        IReadOnlyList<KeyValuePair<string, string>>? properties = null)
    {
        // Build SQL incrementally: FTS join only when query is non-empty,
        // optional vault scope, AND-semantics tag and property filters.
        // Snippet builder gets the original query (or null) and falls back to
        // the first non-empty body line when there's nothing to substring-match.
        using var _ = await AcquireAsync();

        var hasQuery = !string.IsNullOrWhiteSpace(query);
        var sql = new System.Text.StringBuilder();
        var parameters = new List<(string, object)>();

        if (hasQuery)
        {
            sql.Append("""
                SELECT n.path, n.name
                FROM notes_fts
                JOIN notes n ON n.id = notes_fts.rowid
                WHERE notes_fts MATCH @query
                """);
            parameters.Add(("@query", query!));
        }
        else
        {
            sql.Append("""
                SELECT n.path, n.name
                FROM notes n
                WHERE 1=1
                """);
        }

        if (vaultId is not null)
        {
            sql.Append(" AND n.vault_id = @vaultId");
            parameters.Add(("@vaultId", vaultId.Value));
        }

        if (tags is not null)
        {
            for (var i = 0; i < tags.Count; i++)
            {
                var p = $"@tag_{i}";
                sql.Append($" AND EXISTS (SELECT 1 FROM tags WHERE note_id = n.id AND tag = {p})");
                parameters.Add((p, tags[i]));
            }
        }

        if (properties is not null)
        {
            for (var i = 0; i < properties.Count; i++)
            {
                var pk = $"@pk_{i}";
                var pv = $"@pv_{i}";
                sql.Append($" AND EXISTS (SELECT 1 FROM properties WHERE note_id = n.id AND key = {pk} AND value = {pv})");
                parameters.Add((pk, properties[i].Key));
                parameters.Add((pv, properties[i].Value));
            }
        }

        sql.Append(hasQuery ? " ORDER BY rank" : " ORDER BY n.mtime DESC");
        sql.Append(" LIMIT @limit");
        parameters.Add(("@limit", limit));

        var matches = new List<(string Path, string Name)>();
        using (var cmd = Command(sql.ToString(), parameters.ToArray()))
        {
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                matches.Add((reader.GetString(0), reader.GetString(1)));
        }

        return matches
            .Select(m => new SearchResult(m.Path, m.Name, BuildSnippet(m.Path, query)))
            .ToList();
    }

    private static string BuildSnippet(string filePath, string? query)
    {
        try
        {
            var terms = string.IsNullOrWhiteSpace(query)
                ? new List<string>()
                : ExtractSearchTerms(query!);
            string? firstBodyLine = null;
            string? bestLine = null;
            var bestScore = 0;
            // Skip a YAML frontmatter block when picking the fallback line —
            // otherwise filter-only searches show "---" as the snippet.
            var inFrontmatter = false;
            var sawFrontmatterStart = false;
            foreach (var line in File.ReadLines(filePath))
            {
                var trimmed = line.Trim();
                if (!sawFrontmatterStart && trimmed == "---")
                {
                    sawFrontmatterStart = true;
                    inFrontmatter = true;
                    continue;
                }
                if (inFrontmatter)
                {
                    if (trimmed == "---") inFrontmatter = false;
                    continue;
                }
                if (trimmed.Length == 0) continue;
                firstBodyLine ??= TrimSnippet(trimmed);

                if (terms.Count == 0) continue;
                var score = 0;
                foreach (var term in terms)
                    if (trimmed.Contains(term, StringComparison.OrdinalIgnoreCase))
                        score++;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestLine = TrimSnippet(trimmed);
                    if (score == terms.Count) return bestLine; // full match — done
                }
            }
            return bestLine ?? firstBodyLine ?? "";
        }
        catch { return ""; /* file deleted between match and read */ }
    }

    private static string TrimSnippet(string line) =>
        line.Length > 200 ? string.Concat(line.AsSpan(0, 200), "...") : line;

    // Tokenise an FTS5 query into the bare terms a snippet probe should look
    // for. Handles "phrase quotes", AND/OR/NOT/NEAR keywords, column:value
    // qualifiers (column name dropped, value kept), trailing wildcards, and
    // grouping/affinity punctuation. Best-effort — FTS5 grammar is bigger
    // than this, but covers the common cases.
    private static List<string> ExtractSearchTerms(string query)
    {
        var terms = new List<string>();
        var i = 0;
        while (i < query.Length)
        {
            var c = query[i];
            if (char.IsWhiteSpace(c) || c is '(' or ')' or '+' or '-' or '^' or ',')
            {
                i++;
                continue;
            }
            if (c == '"')
            {
                var end = query.IndexOf('"', i + 1);
                if (end < 0) break;
                var phrase = query.Substring(i + 1, end - i - 1).Trim();
                if (phrase.Length > 0) terms.Add(phrase);
                i = end + 1;
                continue;
            }
            var start = i;
            while (i < query.Length && (char.IsLetterOrDigit(query[i]) || query[i] is '_' or '*'))
                i++;
            if (start == i) { i++; continue; }
            // `column:` qualifier — drop the column name; the value is parsed next iteration.
            if (i < query.Length && query[i] == ':')
            {
                i++;
                continue;
            }
            var token = query.Substring(start, i - start);
            if (token is "AND" or "OR" or "NOT" or "NEAR") continue;
            while (token.EndsWith('*')) token = token[..^1];
            if (token.Length > 0) terms.Add(token);
        }
        return terms;
    }

    // -------------------------
    // Tags
    // -------------------------

    public async Task<List<TagCount>> GetTagsAsync(long? vaultId = null)
    {
        using var _ = await AcquireAsync();
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
        using var _ = await AcquireAsync();
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
        using var _ = await AcquireAsync();
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
        using var _ = await AcquireAsync();
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
        using var _ = await AcquireAsync();
        if (_pathToId.TryGetValue(path, out var cached))
            return cached;
        using var cmd = Command("SELECT id FROM notes WHERE path = @path", ("@path", path));
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
        using var _ = await AcquireAsync();
        if (_idToPath.TryGetValue(id, out var cached))
            return cached;
        using var cmd = Command("SELECT path FROM notes WHERE id = @id", ("@id", id));
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
        using var _ = await AcquireAsync();
        var result = new List<string>();
        using var cmd = Command(
            "SELECT path FROM notes WHERE vault_id = @vaultId AND name = @name COLLATE NOCASE",
            ("@vaultId", vaultId), ("@name", name));
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(reader.GetString(0));
        return result;
    }

    // -------------------------
    // Properties
    // -------------------------

    public async Task<List<PropertyEntry>> GetPropertiesAsync(long noteId)
    {
        using var _ = await AcquireAsync();
        var result = new List<PropertyEntry>();
        using var cmd = Command(
            "SELECT key, value FROM properties WHERE note_id = @noteId ORDER BY key",
            ("@noteId", noteId));
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(new PropertyEntry(reader.GetString(0), reader.IsDBNull(1) ? "" : reader.GetString(1)));
        return result;
    }

    public async Task<string?> GetPropertyAsync(long noteId, string key)
    {
        using var _ = await AcquireAsync();
        using var cmd = Command(
            "SELECT value FROM properties WHERE note_id = @noteId AND key = @key",
            ("@noteId", noteId), ("@key", key));
        var result = await cmd.ExecuteScalarAsync();
        return result is string s ? s : null;
    }

    // -------------------------
    // Private helpers
    // -------------------------

    private async Task<IDisposable> AcquireAsync()
    {
        await _gate.WaitAsync();
        return new Releaser(_gate);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _gate;
        public Releaser(SemaphoreSlim gate) => _gate = gate;
        public void Dispose()
        {
            var g = Interlocked.Exchange(ref _gate, null);
            g?.Release();
        }
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
