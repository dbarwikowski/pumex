using Microsoft.Data.Sqlite;
using Pumex.Contracts;

namespace Pumex.Daemon.Tests;

public class NoteRepositoryTests : IDisposable
{
    private readonly TestDbFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    private static NoteDocument Note(
        string path,
        string body = "",
        long mtime = 1,
        IEnumerable<string>? tags = null,
        Dictionary<string, object>? frontmatter = null) => new(
        Path: path,
        Frontmatter: frontmatter ?? new Dictionary<string, object>(),
        Tags: (tags ?? []).ToList(),
        OutgoingLinks: [],
        Content: body,
        RawContent: body,
        Mtime: mtime,
        Size: body.Length);

    // ── basic CRUD ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UpsertNotes_inserts_and_subsequent_calls_update_the_same_row()
    {
        var vaultId = await _fx.Vaults.AddVaultAsync("v", "/v");
        await _fx.UpsertAsync(vaultId, [Note("/v/note.md", body: "hello")]);

        var firstId = await _fx.Notes.GetNoteIdAsync("/v/note.md");
        await _fx.UpsertAsync(vaultId, [Note("/v/note.md", body: "world")]);
        var secondId = await _fx.Notes.GetNoteIdAsync("/v/note.md");

        Assert.Equal(firstId, secondId);
    }

    [Fact]
    public async Task DeleteNote_removes_the_row_and_its_children()
    {
        var vaultId = await _fx.Vaults.AddVaultAsync("v", "/v");
        await _fx.UpsertAsync(vaultId, [Note("/v/note.md", tags: ["alpha"])]);

        await _fx.Notes.DeleteNoteAsync("/v/note.md");

        Assert.Null(await _fx.Notes.GetNoteIdAsync("/v/note.md"));
        Assert.Empty(await _fx.Vaults.GetTagsAsync(vaultId));
    }

    [Fact]
    public async Task GetAllMtimesAsync_returns_path_to_mtime_mapping()
    {
        var vaultId = await _fx.Vaults.AddVaultAsync("v", "/v");
        await _fx.UpsertAsync(vaultId, [Note("/v/a.md", mtime: 100), Note("/v/b.md", mtime: 200)]);

        var mtimes = await _fx.Notes.GetAllMtimesAsync(vaultId);

        Assert.Equal(2, mtimes.Count);
        Assert.Equal(100, mtimes["/v/a.md"]);
        Assert.Equal(200, mtimes["/v/b.md"]);
    }

    [Fact]
    public async Task GetAllPathsAsync_returns_only_paths_in_vault()
    {
        var aId = await _fx.Vaults.AddVaultAsync("a", "/a");
        var bId = await _fx.Vaults.AddVaultAsync("b", "/b");
        await _fx.UpsertAsync(aId, [Note("/a/n.md")]);
        await _fx.UpsertAsync(bId, [Note("/b/n.md")]);

        var paths = await _fx.Notes.GetAllPathsAsync(aId);

        Assert.Single(paths);
        Assert.Equal("/a/n.md", paths[0]);
    }

    [Fact]
    public async Task ListNotesAsync_returns_summaries_sorted_by_mtime_descending()
    {
        var vaultId = await _fx.Vaults.AddVaultAsync("v", "/v");
        await _fx.UpsertAsync(vaultId, [
            Note("/v/old.md",    mtime: 100),
            Note("/v/recent.md", mtime: 200),
            Note("/v/middle.md", mtime: 150),
        ]);

        var notes = await _fx.Notes.ListNotesAsync(vaultId);

        Assert.Equal(["/v/recent.md", "/v/middle.md", "/v/old.md"], notes.Select(n => n.Path));
    }

    [Fact]
    public async Task ListNotesAsync_without_vault_returns_all_vaults()
    {
        var aId = await _fx.Vaults.AddVaultAsync("a", "/a");
        var bId = await _fx.Vaults.AddVaultAsync("b", "/b");
        await _fx.UpsertAsync(aId, [Note("/a/n.md")]);
        await _fx.UpsertAsync(bId, [Note("/b/n.md")]);

        var all = await _fx.Notes.ListNotesAsync();

        Assert.Equal(2, all.Count);
    }

    // ── ID/path cache ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetNoteIdAsync_returns_cached_id_on_second_call()
    {
        var vaultId = await _fx.Vaults.AddVaultAsync("v", "/v");
        await _fx.UpsertAsync(vaultId, [Note("/v/n.md")]);

        var first = await _fx.Notes.GetNoteIdAsync("/v/n.md");
        var second = await _fx.Notes.GetNoteIdAsync("/v/n.md");

        Assert.NotNull(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task GetNoteIdAsync_returns_null_for_unknown_path()
    {
        Assert.Null(await _fx.Notes.GetNoteIdAsync("/ghost.md"));
    }

    [Fact]
    public async Task GetNotePathByIdAsync_returns_path_for_known_id()
    {
        var vaultId = await _fx.Vaults.AddVaultAsync("v", "/v");
        await _fx.UpsertAsync(vaultId, [Note("/v/n.md")]);
        var id = (await _fx.Notes.GetNoteIdAsync("/v/n.md"))!.Value;

        var path = await _fx.Notes.GetNotePathByIdAsync(id);

        Assert.Equal("/v/n.md", path);
    }

    [Fact]
    public async Task GetNotePathsByNameAsync_returns_only_matches_in_the_given_vault()
    {
        var aId = await _fx.Vaults.AddVaultAsync("alpha", "/alpha");
        var bId = await _fx.Vaults.AddVaultAsync("beta", "/beta");
        await _fx.UpsertAsync(aId, [Note("/alpha/today.md"), Note("/alpha/folder/notes-on-today.md")]);
        await _fx.UpsertAsync(bId, [Note("/beta/today.md")]);

        var alpha = await _fx.Notes.GetNotePathsByNameAsync(aId, "today");
        var beta = await _fx.Notes.GetNotePathsByNameAsync(bId, "today");

        Assert.Single(alpha);
        Assert.Single(beta);
        Assert.Equal("/alpha/today.md", alpha[0]);
        Assert.Equal("/beta/today.md", beta[0]);
    }

    [Fact]
    public async Task GetNotePathsByNameAsync_is_case_insensitive()
    {
        var vaultId = await _fx.Vaults.AddVaultAsync("v", "/v");
        await _fx.UpsertAsync(vaultId, [Note("/v/Mixed-Case.md")]);

        var lower = await _fx.Notes.GetNotePathsByNameAsync(vaultId, "mixed-case");
        var upper = await _fx.Notes.GetNotePathsByNameAsync(vaultId, "MIXED-CASE");

        Assert.Single(lower);
        Assert.Single(upper);
    }

    // ── properties ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPropertiesAsync_returns_frontmatter_keys_in_alphabetical_order()
    {
        var vaultId = await _fx.Vaults.AddVaultAsync("v", "/v");
        await _fx.UpsertAsync(vaultId, [Note("/v/n.md", frontmatter: new()
        {
            ["zeta"]  = "last",
            ["alpha"] = "first",
            ["mu"]    = "middle",
        })]);
        var noteId = (await _fx.Notes.GetNoteIdAsync("/v/n.md"))!.Value;

        var props = await _fx.Notes.GetPropertiesAsync(noteId);

        Assert.Equal(["alpha", "mu", "zeta"], props.Select(p => p.Key));
        Assert.Equal("first",  props[0].Value);
        Assert.Equal("middle", props[1].Value);
        Assert.Equal("last",   props[2].Value);
    }

    [Fact]
    public async Task GetPropertyAsync_returns_value_or_null()
    {
        var vaultId = await _fx.Vaults.AddVaultAsync("v", "/v");
        await _fx.UpsertAsync(vaultId, [Note("/v/n.md", frontmatter: new() { ["status"] = "draft" })]);
        var noteId = (await _fx.Notes.GetNoteIdAsync("/v/n.md"))!.Value;

        Assert.Equal("draft", await _fx.Notes.GetPropertyAsync(noteId, "status"));
        Assert.Null(await _fx.Notes.GetPropertyAsync(noteId, "missing"));
    }

    // ── legacy FTS migration ─────────────────────────────────────────────────

    [Fact]
    public void Constructor_migrates_legacy_fts_schema_and_resets_mtimes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pumex-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var legacyDbPath = Path.Combine(dir, "index.db");
        try
        {
            // Create a DB with the old FTS schema (no contentless_delete).
            using (var raw = new SqliteConnection($"Data Source={legacyDbPath}"))
            {
                raw.Open();
                using var seed = raw.CreateCommand();
                seed.CommandText = """
                    CREATE TABLE vaults (id INTEGER PRIMARY KEY, name TEXT UNIQUE NOT NULL, path TEXT UNIQUE NOT NULL);
                    CREATE TABLE notes (id INTEGER PRIMARY KEY, vault_id INTEGER, path TEXT UNIQUE NOT NULL,
                                        name TEXT NOT NULL, mtime INTEGER NOT NULL, size INTEGER NOT NULL);
                    CREATE VIRTUAL TABLE notes_fts USING fts5(name, body, content='', tokenize='unicode61');
                    INSERT INTO vaults(id, name, path) VALUES (1, 'v', '/v');
                    INSERT INTO notes(id, vault_id, path, name, mtime, size) VALUES (1, 1, '/v/a.md', 'a', 12345, 0);
                    """;
                seed.ExecuteNonQuery();
            }

            using var ctx = new IndexDbContext(legacyDbPath);
            new IndexSchema(ctx).Apply();

            using var check = new SqliteConnection($"Data Source={legacyDbPath}");
            check.Open();
            using var schemaCmd = check.CreateCommand();
            schemaCmd.CommandText = "SELECT sql FROM sqlite_master WHERE name = 'notes_fts'";
            var sql = (string?)schemaCmd.ExecuteScalar();
            Assert.Contains("contentless_delete", sql!, StringComparison.OrdinalIgnoreCase);

            using var mtimeCmd = check.CreateCommand();
            mtimeCmd.CommandText = "SELECT mtime FROM notes WHERE id = 1";
            Assert.Equal(0L, (long)mtimeCmd.ExecuteScalar()!);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
