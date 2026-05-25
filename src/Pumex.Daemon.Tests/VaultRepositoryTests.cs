using Pumex.Contracts;

namespace Pumex.Daemon.Tests;

public class VaultRepositoryTests : IDisposable
{
    private readonly TestDbFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    private static NoteDocument Note(string path, IEnumerable<string>? tags = null) => new(
        Path: path,
        Frontmatter: new Dictionary<string, object>(),
        Tags: (tags ?? []).ToList(),
        OutgoingLinks: [],
        Content: "",
        RawContent: "",
        Mtime: 1,
        Size: 0);

    // ── vault CRUD ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AddVault_then_lookup_round_trips_by_path_and_name()
    {
        await _fx.Vaults.AddVaultAsync("alpha", "/some/path/alpha");

        var byPath = await _fx.Vaults.GetVaultByPathAsync("/some/path/alpha");
        var byName = await _fx.Vaults.GetVaultByNameAsync("alpha");

        Assert.NotNull(byPath);
        Assert.NotNull(byName);
        Assert.Equal(byPath!.Id, byName!.Id);
        Assert.Equal("alpha", byPath.Name);
    }

    [Fact]
    public async Task GetVaultByName_returns_null_when_absent()
    {
        Assert.Null(await _fx.Vaults.GetVaultByNameAsync("ghost"));
    }

    [Fact]
    public async Task GetVaultsAsync_returns_all_registered_vaults()
    {
        await _fx.Vaults.AddVaultAsync("a", "/a");
        await _fx.Vaults.AddVaultAsync("b", "/b");

        var all = await _fx.Vaults.GetVaultsAsync();

        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task RemoveVaultAsync_cascades_to_notes_tags_properties_and_links()
    {
        var vaultId = await _fx.Vaults.AddVaultAsync("doomed", "/doomed");
        await _fx.UpsertAsync(vaultId, [Note("/doomed/n.md", tags: ["work"])]);
        Assert.Single(await _fx.Vaults.GetTagsAsync(vaultId));

        await _fx.Vaults.RemoveVaultAsync(vaultId);

        Assert.Null(await _fx.Vaults.GetVaultByNameAsync("doomed"));
        Assert.Empty(await _fx.Vaults.GetTagsAsync(vaultId));
        Assert.Empty(await _fx.Notes.GetAllPathsAsync(vaultId));
    }

    [Fact]
    public async Task RemoveVaultAsync_leaves_other_vaults_untouched()
    {
        var aId = await _fx.Vaults.AddVaultAsync("a", "/a");
        var bId = await _fx.Vaults.AddVaultAsync("b", "/b");
        await _fx.UpsertAsync(aId, [Note("/a/n.md", tags: ["a-tag"])]);
        await _fx.UpsertAsync(bId, [Note("/b/n.md", tags: ["b-tag"])]);

        await _fx.Vaults.RemoveVaultAsync(aId);

        Assert.Null(await _fx.Vaults.GetVaultByNameAsync("a"));
        Assert.NotNull(await _fx.Vaults.GetVaultByNameAsync("b"));
        var bTags = await _fx.Vaults.GetTagsAsync(bId);
        Assert.Single(bTags);
        Assert.Equal("b-tag", bTags[0].Tag);
    }

    [Fact]
    public async Task RemoveVaultAsync_evicts_note_ids_from_cache()
    {
        var vaultId = await _fx.Vaults.AddVaultAsync("v", "/v");
        await _fx.UpsertAsync(vaultId, [Note("/v/n.md")]);
        // Warm the cache by looking up the id before removal.
        var idBefore = await _fx.Notes.GetNoteIdAsync("/v/n.md");
        Assert.NotNull(idBefore);

        await _fx.Vaults.RemoveVaultAsync(vaultId);

        // After removal the note no longer exists in the DB; GetNoteIdAsync must
        // return null (not a stale cache hit).
        Assert.Null(await _fx.Notes.GetNoteIdAsync("/v/n.md"));
    }

    // ── tags ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTagsAsync_scopes_to_vault_when_id_is_supplied()
    {
        var aId = await _fx.Vaults.AddVaultAsync("alpha", "/alpha");
        var bId = await _fx.Vaults.AddVaultAsync("beta", "/beta");
        await _fx.UpsertAsync(aId, [Note("/alpha/a.md", tags: ["shared", "alpha-only"])]);
        await _fx.UpsertAsync(bId, [Note("/beta/b.md", tags: ["shared", "beta-only"])]);

        var global = await _fx.Vaults.GetTagsAsync();
        var alpha = await _fx.Vaults.GetTagsAsync(aId);

        Assert.Equal(3, global.Count);
        Assert.Equal(2, alpha.Count);
        Assert.DoesNotContain(alpha, t => t.Tag == "beta-only");
    }
}
