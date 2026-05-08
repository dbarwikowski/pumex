namespace Pumex.Daemon.Tests;

public class IndexDbTests : IDisposable
{
    private readonly string _dbPath;
    private readonly IndexDb _db;

    public IndexDbTests()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pumex-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "index.db");
        _db = new IndexDb(_dbPath);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(Path.GetDirectoryName(_dbPath)!, recursive: true); }
        catch { /* test cleanup is best-effort */ }
    }

    private static NoteDocument MakeNote(
        string path,
        string body = "",
        IEnumerable<string>? tags = null,
        IEnumerable<string>? outgoing = null,
        Dictionary<string, object>? frontmatter = null)
        => new(
            Path: path,
            Frontmatter: frontmatter ?? new Dictionary<string, object>(),
            Tags: (tags ?? []).ToList(),
            OutgoingLinks: (outgoing ?? []).ToList(),
            Content: body,
            RawContent: body,
            Mtime: 1,
            Size: body.Length);

    [Fact]
    public async Task AddVault_then_lookup_round_trips_by_path_and_name()
    {
        await _db.AddVaultAsync("alpha", "/some/path/alpha");

        var byPath = await _db.GetVaultByPathAsync("/some/path/alpha");
        var byName = await _db.GetVaultByNameAsync("alpha");

        Assert.NotNull(byPath);
        Assert.NotNull(byName);
        Assert.Equal(byPath!.Id, byName!.Id);
        Assert.Equal("alpha", byPath.Name);
    }

    [Fact]
    public async Task GetVaultByName_returns_null_when_absent()
    {
        Assert.Null(await _db.GetVaultByNameAsync("ghost"));
    }

    [Fact]
    public async Task UpsertNotes_inserts_and_subsequent_calls_update_the_same_row()
    {
        var vaultId = await _db.AddVaultAsync("v", "/v");
        var note = MakeNote("/v/note.md", body: "hello");
        await _db.UpsertNotesAsync(vaultId, new[] { note });

        var firstId = await _db.GetNoteIdAsync("/v/note.md");
        await _db.UpsertNotesAsync(vaultId, new[] { MakeNote("/v/note.md", body: "world") });
        var secondId = await _db.GetNoteIdAsync("/v/note.md");

        Assert.Equal(firstId, secondId);
    }

    [Fact]
    public async Task DeleteNote_removes_the_row_and_its_children()
    {
        var vaultId = await _db.AddVaultAsync("v", "/v");
        var note = MakeNote("/v/note.md", tags: ["alpha"]);
        await _db.UpsertNotesAsync(vaultId, new[] { note });

        await _db.DeleteNoteAsync("/v/note.md");

        Assert.Null(await _db.GetNoteIdAsync("/v/note.md"));
        Assert.Empty(await _db.GetTagsAsync(vaultId));
    }

    [Fact]
    public async Task SearchAsync_finds_notes_via_fts_index()
    {
        var vaultId = await _db.AddVaultAsync("v", "/v");
        await _db.UpsertNotesAsync(vaultId, new[]
        {
            MakeNote("/v/match.md", body: "the quick brown fox"),
            MakeNote("/v/miss.md",  body: "lazy dog by the river"),
        });

        var hits = await _db.SearchAsync("fox");

        Assert.Single(hits);
        Assert.Equal("/v/match.md", hits[0].Path);
    }

    [Fact]
    public async Task SearchAsync_scopes_to_vault_when_id_is_supplied()
    {
        var aId = await _db.AddVaultAsync("alpha", "/alpha");
        var bId = await _db.AddVaultAsync("beta",  "/beta");
        await _db.UpsertNotesAsync(aId, new[] { MakeNote("/alpha/n.md", body: "shared keyword zebrafinch") });
        await _db.UpsertNotesAsync(bId, new[] { MakeNote("/beta/n.md",  body: "shared keyword zebrafinch") });

        var globalHits = await _db.SearchAsync("zebrafinch");
        var alphaHits  = await _db.SearchAsync("zebrafinch", vaultId: aId);
        var betaHits   = await _db.SearchAsync("zebrafinch", vaultId: bId);

        Assert.Equal(2, globalHits.Count);
        Assert.Single(alphaHits);
        Assert.Single(betaHits);
        Assert.Equal("/alpha/n.md", alphaHits[0].Path);
        Assert.Equal("/beta/n.md",  betaHits[0].Path);
    }

    [Fact]
    public async Task GetTagsAsync_scopes_to_vault_when_id_is_supplied()
    {
        var aId = await _db.AddVaultAsync("alpha", "/alpha");
        var bId = await _db.AddVaultAsync("beta",  "/beta");
        await _db.UpsertNotesAsync(aId, new[] { MakeNote("/alpha/a.md", tags: ["shared", "alpha-only"]) });
        await _db.UpsertNotesAsync(bId, new[] { MakeNote("/beta/b.md",  tags: ["shared", "beta-only"]) });

        var global = await _db.GetTagsAsync();
        var alpha  = await _db.GetTagsAsync(aId);

        Assert.Equal(3, global.Count);
        Assert.Equal(2, alpha.Count);
        Assert.DoesNotContain(alpha, t => t.Tag == "beta-only");
    }

    [Fact]
    public async Task GetBacklinksAsync_returns_resolved_sources()
    {
        var vaultId = await _db.AddVaultAsync("v", "/v");
        await _db.UpsertNotesAsync(vaultId, new[]
        {
            MakeNote("/v/source.md", outgoing: ["target"]),
            MakeNote("/v/target.md"),
        });
        var sourceId = (await _db.GetNoteIdAsync("/v/source.md"))!.Value;
        var targetId = (await _db.GetNoteIdAsync("/v/target.md"))!.Value;
        await _db.SetLinkResolutionAsync(sourceId, "target", targetId);

        var backlinks = await _db.GetBacklinksAsync("/v/target.md");

        Assert.Single(backlinks);
        Assert.Equal("/v/source.md", backlinks[0]);
    }

    [Fact]
    public async Task GetBacklinksAsync_filters_to_one_vault()
    {
        var aId = await _db.AddVaultAsync("alpha", "/alpha");
        var bId = await _db.AddVaultAsync("beta",  "/beta");

        // Two source notes (one per vault) link to the same target path string.
        await _db.UpsertNotesAsync(aId, new[]
        {
            MakeNote("/alpha/src.md", outgoing: ["target"]),
            MakeNote("/alpha/target.md"),
        });
        await _db.UpsertNotesAsync(bId, new[]
        {
            MakeNote("/beta/src.md", outgoing: ["../alpha/target"]),
        });
        var alphaSrc   = (await _db.GetNoteIdAsync("/alpha/src.md"))!.Value;
        var alphaTgt   = (await _db.GetNoteIdAsync("/alpha/target.md"))!.Value;
        var betaSrc    = (await _db.GetNoteIdAsync("/beta/src.md"))!.Value;
        await _db.SetLinkResolutionAsync(alphaSrc, "target",            alphaTgt);
        await _db.SetLinkResolutionAsync(betaSrc,  "../alpha/target",   alphaTgt);

        var global = await _db.GetBacklinksAsync("/alpha/target.md");
        var alphaOnly = await _db.GetBacklinksAsync("/alpha/target.md", vaultId: aId);

        Assert.Equal(2, global.Count);
        Assert.Single(alphaOnly);
        Assert.Equal("/alpha/src.md", alphaOnly[0]);
    }

    [Fact]
    public async Task GetPropertiesAsync_returns_frontmatter_keys_in_alphabetical_order()
    {
        var vaultId = await _db.AddVaultAsync("v", "/v");
        var note = MakeNote("/v/n.md", frontmatter: new Dictionary<string, object>
        {
            ["zeta"]  = "last",
            ["alpha"] = "first",
            ["mu"]    = "middle",
        });
        await _db.UpsertNotesAsync(vaultId, [note]);
        var noteId = (await _db.GetNoteIdAsync("/v/n.md"))!.Value;

        var props = await _db.GetPropertiesAsync(noteId);

        Assert.Equal(new[] { "alpha", "mu", "zeta" }, props.Select(p => p.Key));
        Assert.Equal("first",  props[0].Value);
        Assert.Equal("middle", props[1].Value);
        Assert.Equal("last",   props[2].Value);
    }

    [Fact]
    public async Task GetPropertyAsync_returns_value_or_null()
    {
        var vaultId = await _db.AddVaultAsync("v", "/v");
        var note = MakeNote("/v/n.md", frontmatter: new Dictionary<string, object>
        {
            ["status"] = "draft",
        });
        await _db.UpsertNotesAsync(vaultId, [note]);
        var noteId = (await _db.GetNoteIdAsync("/v/n.md"))!.Value;

        Assert.Equal("draft", await _db.GetPropertyAsync(noteId, "status"));
        Assert.Null(await _db.GetPropertyAsync(noteId, "missing"));
    }

    [Fact]
    public async Task GetNotePathsByNameAsync_returns_only_matches_in_the_given_vault()
    {
        var aId = await _db.AddVaultAsync("alpha", "/alpha");
        var bId = await _db.AddVaultAsync("beta",  "/beta");
        await _db.UpsertNotesAsync(aId, new[]
        {
            MakeNote("/alpha/today.md"),
            MakeNote("/alpha/folder/notes-on-today.md"),
        });
        await _db.UpsertNotesAsync(bId, new[] { MakeNote("/beta/today.md") });

        var alpha = await _db.GetNotePathsByNameAsync(aId, "today");
        var beta  = await _db.GetNotePathsByNameAsync(bId, "today");

        Assert.Single(alpha);
        Assert.Single(beta);
        Assert.Equal("/alpha/today.md", alpha[0]);
        Assert.Equal("/beta/today.md",  beta[0]);
    }

    [Fact]
    public async Task GetNotePathsByNameAsync_is_case_insensitive()
    {
        var vaultId = await _db.AddVaultAsync("v", "/v");
        await _db.UpsertNotesAsync(vaultId, new[] { MakeNote("/v/Mixed-Case.md") });

        var lower = await _db.GetNotePathsByNameAsync(vaultId, "mixed-case");
        var upper = await _db.GetNotePathsByNameAsync(vaultId, "MIXED-CASE");

        Assert.Single(lower);
        Assert.Single(upper);
    }

    [Fact]
    public async Task GetUnresolvedLinksAsync_returns_only_unresolved_links_for_the_vault()
    {
        var aId = await _db.AddVaultAsync("alpha", "/alpha");
        var bId = await _db.AddVaultAsync("beta",  "/beta");
        await _db.UpsertNotesAsync(aId, new[] { MakeNote("/alpha/src.md", outgoing: ["unknown-a"]) });
        await _db.UpsertNotesAsync(bId, new[] { MakeNote("/beta/src.md",  outgoing: ["unknown-b"]) });

        var alpha = await _db.GetUnresolvedLinksAsync(aId);

        Assert.Single(alpha);
        Assert.Equal("unknown-a", alpha[0].TargetText);
    }
}
