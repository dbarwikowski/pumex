using Pumex.Contracts;

namespace Pumex.Daemon.Tests;

public class SearchRepositoryTests : IDisposable
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

    [Fact]
    public async Task SearchAsync_finds_notes_via_fts_index()
    {
        var vaultId = await _fx.Vaults.AddVaultAsync("v", "/v");
        await _fx.UpsertAsync(vaultId, [
            Note("/v/match.md", body: "the quick brown fox"),
            Note("/v/miss.md",  body: "lazy dog by the river"),
        ]);

        var hits = await _fx.Search.SearchAsync("fox");

        Assert.Single(hits);
        Assert.Equal("/v/match.md", hits[0].Path);
    }

    [Fact]
    public async Task SearchAsync_filters_by_required_tag()
    {
        var vaultId = await _fx.Vaults.AddVaultAsync("v", "/v");
        await _fx.UpsertAsync(vaultId, [
            Note("/v/tagged.md",   body: "shared keyword zebrafinch", tags: ["work"]),
            Note("/v/untagged.md", body: "shared keyword zebrafinch"),
        ]);

        var hits = await _fx.Search.SearchAsync("zebrafinch", vaultId: vaultId, tags: ["work"]);

        Assert.Single(hits);
        Assert.Equal("/v/tagged.md", hits[0].Path);
    }

    [Fact]
    public async Task SearchAsync_with_multiple_tags_requires_all_of_them()
    {
        var vaultId = await _fx.Vaults.AddVaultAsync("v", "/v");
        await _fx.UpsertAsync(vaultId, [
            Note("/v/both.md",    body: "z", tags: ["work", "urgent"]),
            Note("/v/oneonly.md", body: "z", tags: ["work"]),
        ]);

        var hits = await _fx.Search.SearchAsync("z", vaultId: vaultId, tags: ["work", "urgent"]);

        Assert.Single(hits);
        Assert.Equal("/v/both.md", hits[0].Path);
    }

    [Fact]
    public async Task SearchAsync_filters_by_property_key_and_value()
    {
        var vaultId = await _fx.Vaults.AddVaultAsync("v", "/v");
        await _fx.UpsertAsync(vaultId, [
            Note("/v/draft.md",     body: "z", frontmatter: new() { ["status"] = "draft" }),
            Note("/v/published.md", body: "z", frontmatter: new() { ["status"] = "published" }),
        ]);

        var hits = await _fx.Search.SearchAsync("z", vaultId: vaultId,
            properties: [new KeyValuePair<string, string>("status", "draft")]);

        Assert.Single(hits);
        Assert.Equal("/v/draft.md", hits[0].Path);
    }

    [Fact]
    public async Task SearchAsync_works_without_a_query_returning_filtered_notes()
    {
        var vaultId = await _fx.Vaults.AddVaultAsync("v", "/v");
        await _fx.UpsertAsync(vaultId, [
            Note("/v/a.md", tags: ["work"], mtime: 200),
            Note("/v/b.md", tags: ["work"], mtime: 100),
            Note("/v/c.md", tags: ["other"]),
        ]);

        var hits = await _fx.Search.SearchAsync(null, vaultId: vaultId, tags: ["work"]);

        Assert.Equal(2, hits.Count);
        Assert.Equal("/v/a.md", hits[0].Path); // newer first
        Assert.Equal("/v/b.md", hits[1].Path);
    }

    [Fact]
    public async Task SearchAsync_scopes_to_vault_when_id_is_supplied()
    {
        var aId = await _fx.Vaults.AddVaultAsync("alpha", "/alpha");
        var bId = await _fx.Vaults.AddVaultAsync("beta",  "/beta");
        await _fx.UpsertAsync(aId, [Note("/alpha/n.md", body: "shared keyword zebrafinch")]);
        await _fx.UpsertAsync(bId, [Note("/beta/n.md",  body: "shared keyword zebrafinch")]);

        var globalHits = await _fx.Search.SearchAsync("zebrafinch");
        var alphaHits  = await _fx.Search.SearchAsync("zebrafinch", vaultId: aId);
        var betaHits   = await _fx.Search.SearchAsync("zebrafinch", vaultId: bId);

        Assert.Equal(2, globalHits.Count);
        Assert.Single(alphaHits);
        Assert.Single(betaHits);
        Assert.Equal("/alpha/n.md", alphaHits[0].Path);
        Assert.Equal("/beta/n.md",  betaHits[0].Path);
    }
}
