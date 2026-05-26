using Microsoft.Data.Sqlite;

namespace Pumex.Daemon;

/// <summary>
/// Applies WAL pragmas, migrates legacy FTS tables, and creates the full schema
/// on first run. Called once at daemon startup — not registered in DI.
/// </summary>
public class IndexSchema(IndexDbContext context)
{
    public void Apply()
    {
        ApplyPragmas();
        MigrateLegacyFts();
        EnsureSchema();
    }

    private void ApplyPragmas()
    {
        context.Execute("PRAGMA journal_mode=WAL");
        context.Execute("PRAGMA synchronous=NORMAL");
        context.Execute("PRAGMA foreign_keys=ON");
    }

    private void EnsureSchema() => context.Execute("""
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
        -- computed lazily from disk. contentless_delete=1 lets us issue
        -- plain DELETE FROM notes_fts without supplying the original
        -- column values (without it, the FTS doclist gets corrupted on
        -- cascade delete from vaults — SQLITE_CORRUPT).
        CREATE VIRTUAL TABLE IF NOT EXISTS notes_fts USING fts5(
            name,
            body,
            content='',
            contentless_delete=1,
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

        CREATE INDEX IF NOT EXISTS idx_tags_tag          ON tags(tag);
        CREATE INDEX IF NOT EXISTS idx_tags_note         ON tags(note_id);
        CREATE INDEX IF NOT EXISTS idx_links_source      ON links(source_id);
        CREATE INDEX IF NOT EXISTS idx_links_target      ON links(resolved_id);
        CREATE INDEX IF NOT EXISTS idx_links_target_path ON links(target_path);
        CREATE INDEX IF NOT EXISTS idx_notes_mtime       ON notes(mtime);
        CREATE INDEX IF NOT EXISTS idx_notes_vault       ON notes(vault_id);

        -- Purge FTS rowids when notes are deleted (explicit DELETE *and*
        -- cascade from vaults). Without this, RemoveVaultAsync leaves FTS
        -- orphans that grow the index file over time. Requires
        -- contentless_delete=1 on notes_fts.
        CREATE TRIGGER IF NOT EXISTS notes_fts_purge_after_delete
        AFTER DELETE ON notes BEGIN
            DELETE FROM notes_fts WHERE rowid = old.id;
        END;
        """);

    // Pre-3.43-style contentless FTS5 tables don't support plain DELETE and
    // their 'delete'-command-with-empty-strings substitute corrupts the
    // doclist on cascade DELETE. If we find such a table, drop it (and the
    // old trigger) so EnsureSchema can recreate them with
    // contentless_delete=1, then zero out notes.mtime so the next full scan
    // re-indexes every note's body into the empty FTS.
    private void MigrateLegacyFts()
    {
        if (!TableExists("notes_fts")) return;

        string? createSql;
        using (var cmd = context.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'notes_fts'";
            createSql = cmd.ExecuteScalar() as string;
        }
        if (createSql is null || createSql.Contains("contentless_delete", StringComparison.OrdinalIgnoreCase))
            return;

        context.Execute("""
            DROP TRIGGER IF EXISTS notes_fts_purge_after_delete;
            DROP TABLE notes_fts;
            UPDATE notes SET mtime = 0;
            """);
    }

    private bool TableExists(string name)
    {
        using var cmd = context.Connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = @name";
        cmd.Parameters.AddWithValue("@name", name);
        return cmd.ExecuteScalar() is not null;
    }
}
