using Pumex.Contracts;

namespace Pumex.Daemon.Tests;

public class LinkRepositoryTests : IDisposable
{
    private readonly TestDbFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    private static NoteDocument Note(string path, string body = "", IEnumerable<string>? outgoing = null) => new(
        Path: path,
        Frontmatter: new Dictionary<string, object>(),
        Tags: [],
        OutgoingLinks: (outgoing ?? []).ToList(),
        Content: body,
        RawContent: body,
        Mtime: 1,
        Size: body.Length);

    [Fact]
    public async Task GetBacklinksAsync_returns_resolved_sources()
    {
        var vaultId = await _fx.Vaults.AddVaultAsync("v", "/v");
        await _fx.UpsertAsync(vaultId, [
            Note("/v/source.md", outgoing: ["target"]),
            Note("/v/target.md"),
        ]);
        var sourceId = (await _fx.Notes.GetNoteIdAsync("/v/source.md"))!.Value;
        var targetId = (await _fx.Notes.GetNoteIdAsync("/v/target.md"))!.Value;
        await _fx.Links.SetLinkResolutionAsync(sourceId, "target", targetId);

        var backlinks = await _fx.Links.GetBacklinksAsync("/v/target.md");

        Assert.Single(backlinks);
        Assert.Equal("/v/source.md", backlinks[0]);
    }

    [Fact]
    public async Task GetBacklinksAsync_filters_to_one_vault()
    {
        var aId = await _fx.Vaults.AddVaultAsync("alpha", "/alpha");
        var bId = await _fx.Vaults.AddVaultAsync("beta",  "/beta");
        await _fx.UpsertAsync(aId, [
            Note("/alpha/src.md", outgoing: ["target"]),
            Note("/alpha/target.md"),
        ]);
        await _fx.UpsertAsync(bId, [
            Note("/beta/src.md", outgoing: ["../alpha/target"]),
        ]);
        var alphaSrc = (await _fx.Notes.GetNoteIdAsync("/alpha/src.md"))!.Value;
        var alphaTgt = (await _fx.Notes.GetNoteIdAsync("/alpha/target.md"))!.Value;
        var betaSrc  = (await _fx.Notes.GetNoteIdAsync("/beta/src.md"))!.Value;
        await _fx.Links.SetLinkResolutionAsync(alphaSrc, "target",           alphaTgt);
        await _fx.Links.SetLinkResolutionAsync(betaSrc,  "../alpha/target",  alphaTgt);

        var global    = await _fx.Links.GetBacklinksAsync("/alpha/target.md");
        var alphaOnly = await _fx.Links.GetBacklinksAsync("/alpha/target.md", vaultId: aId);

        Assert.Equal(2, global.Count);
        Assert.Single(alphaOnly);
        Assert.Equal("/alpha/src.md", alphaOnly[0]);
    }

    [Fact]
    public async Task GetUnresolvedLinksAsync_returns_only_unresolved_links_for_the_vault()
    {
        var aId = await _fx.Vaults.AddVaultAsync("alpha", "/alpha");
        var bId = await _fx.Vaults.AddVaultAsync("beta",  "/beta");
        await _fx.UpsertAsync(aId, [Note("/alpha/src.md", outgoing: ["unknown-a"])]);
        await _fx.UpsertAsync(bId, [Note("/beta/src.md",  outgoing: ["unknown-b"])]);

        var alpha = await _fx.Links.GetUnresolvedLinksAsync(aId);

        Assert.Single(alpha);
        Assert.Equal("unknown-a", alpha[0].TargetText);
    }

    [Fact]
    public async Task FTS_remains_searchable_after_vault_removal_with_indexed_bodies()
    {
        // Regression: corrupt FTS doclist after cascade DELETE from vaults.
        var vaultId = await _fx.Vaults.AddVaultAsync("doomed", "/doomed");
        var notes = Enumerable.Range(0, 25)
            .Select(i => Note($"/doomed/n{i}.md", body: $"seed body {i} doomed"))
            .ToList();
        await _fx.UpsertAsync(vaultId, notes);

        await _fx.Vaults.RemoveVaultAsync(vaultId);

        Assert.Empty(await _fx.Notes.GetAllPathsAsync(vaultId));
        // FTS must be intact after vault removal.
        var freshId = await _fx.Vaults.AddVaultAsync("fresh", "/fresh");
        await _fx.UpsertAsync(freshId, [new NoteDocument(
            Path: "/fresh/n.md",
            Frontmatter: new Dictionary<string, object>(),
            Tags: [],
            OutgoingLinks: [],
            Content: "zebrafinch",
            RawContent: "zebrafinch",
            Mtime: 1,
            Size: 10)]);
        Assert.Single(await _fx.Search.SearchAsync("zebrafinch"));
    }
}
