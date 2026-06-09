using Pumex.Contracts;

namespace Pumex.Daemon.Tests;

public class ContextRepositoryTests : IDisposable
{
    private readonly TestDbFixture _fx = new();
    private readonly string _vaultDir;
    private readonly ContextRepository _ctx;

    public ContextRepositoryTests()
    {
        _vaultDir = Path.Combine(Path.GetTempPath(), "pumex-ctx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_vaultDir);
        _ctx = new ContextRepository(_fx.Context);
    }

    public void Dispose()
    {
        _fx.Dispose();
        try { Directory.Delete(_vaultDir, recursive: true); } catch { /* best-effort */ }
    }

    // Writes each file to disk under the vault and indexes it. Passages are read
    // from disk; the FTS body is the on-disk text minus any frontmatter block.
    private async Task<long> SeedAsync(params (string rel, string content)[] files)
    {
        var vaultId = await _fx.Vaults.AddVaultAsync("v", _vaultDir);
        var docs = new List<NoteDocument>();
        foreach (var (rel, content) in files)
        {
            var path = Path.Combine(_vaultDir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content);
            docs.Add(new NoteDocument(
                Path: path,
                Frontmatter: new Dictionary<string, object>(),
                Tags: [],
                OutgoingLinks: [],
                Content: StripFrontmatter(content),
                RawContent: content,
                Mtime: 1,
                Size: content.Length));
        }
        await _fx.UpsertAsync(vaultId, docs);
        return vaultId;
    }

    private static string StripFrontmatter(string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---") return content;
        var end = Array.IndexOf(lines, "---", 1);
        return end < 0 ? content : string.Join('\n', lines[(end + 1)..]);
    }

    [Fact]
    public async Task ContextAsync_returns_ranked_passages_with_unique_name_pointer()
    {
        await SeedAsync(
            ("architecture.md", "# Indexing\n\nThe indexer handles config changes by reloading the policy and rescanning."),
            ("cats.md", "# Cats\n\nUnrelated content about cats and naps."));

        var results = await _ctx.ContextAsync("how does the indexer handle config changes");

        Assert.Single(results);
        var r = results[0];
        Assert.Equal("architecture.md", r.RelativePath);
        Assert.Contains("indexer handles config changes", r.Passage);
        Assert.Equal("Indexing", r.Passage.Split('\n')[0]); // heading prepended, marks stripped
        Assert.Equal("architecture", r.Pointer);            // unique name → bare pointer
    }

    [Fact]
    public async Task ContextAsync_normalises_score_so_higher_is_better()
    {
        // 5-doc corpus, "alpha" in 2 of them, so bm25 IDF is non-zero (in a
        // 2-doc corpus it collapses to 0). The note matching more often must
        // score higher and rank first.
        await SeedAsync(
            ("strong.md", "alpha alpha alpha here"),
            ("weak.md", "alpha solo mention"),
            ("f1.md", "beta only"),
            ("f2.md", "gamma only"),
            ("f3.md", "delta only"));

        var results = await _ctx.ContextAsync("alpha");

        Assert.Equal(2, results.Count);
        Assert.Equal("strong.md", results[0].RelativePath);
        Assert.True(results[0].Score > results[1].Score,
            $"higher relevance should yield higher score ({results[0].Score} vs {results[1].Score})");
        Assert.True(results[0].Score > 0, "top score should be positive");
    }

    [Fact]
    public async Task ContextAsync_uses_relative_path_pointer_when_name_is_ambiguous()
    {
        await SeedAsync(
            ("a/data.md", "shared keyword zebrafinch here"),
            ("b/data.md", "shared keyword zebrafinch there"));

        var results = await _ctx.ContextAsync("zebrafinch");

        Assert.Equal(2, results.Count);
        Assert.Equal(
            new[] { "a/data.md", "b/data.md" },
            results.Select(r => r.Pointer).OrderBy(p => p).ToArray());
    }

    [Fact]
    public async Task ContextAsync_drops_lowest_ranked_sources_when_over_budget()
    {
        // "top" matches the term four times → ranks first; the others once each.
        await SeedAsync(
            ("top.md", "alpha alpha alpha alpha"),
            ("mid.md", "alpha beta gamma delta"),
            ("low.md", "alpha epsilon zeta eta"));

        // Budget fits only the first passage; lower-ranked sources are dropped whole.
        var results = await _ctx.ContextAsync("alpha", budgetChars: 23);

        Assert.Single(results);
        Assert.Equal("top.md", results[0].RelativePath);
    }

    [Fact]
    public async Task ContextAsync_respects_limit()
    {
        await SeedAsync(
            ("one.md", "keyword one"),
            ("two.md", "keyword two"),
            ("three.md", "keyword three"));

        var results = await _ctx.ContextAsync("keyword", limit: 2);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task ContextAsync_skips_frontmatter_in_passage()
    {
        await SeedAsync(
            ("fm.md", "---\ntitle: Secret\ntags: [x]\n---\n\nThe body mentions zebrafinch clearly."));

        var results = await _ctx.ContextAsync("zebrafinch");

        Assert.Single(results);
        Assert.Equal("The body mentions zebrafinch clearly.", results[0].Passage);
        Assert.DoesNotContain("Secret", results[0].Passage);
        Assert.DoesNotContain("---", results[0].Passage);
    }

    [Fact]
    public async Task ContextAsync_returns_empty_when_nothing_matches()
    {
        await SeedAsync(("x.md", "apples and oranges"));

        var results = await _ctx.ContextAsync("bananas");

        Assert.Empty(results);
    }

    [Fact]
    public async Task ContextAsync_scopes_to_vault_when_id_supplied()
    {
        var aId = await _fx.Vaults.AddVaultAsync("alpha", _vaultDir);
        var bDir = Path.Combine(Path.GetTempPath(), "pumex-ctx-b-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bDir);
        try
        {
            var bId = await _fx.Vaults.AddVaultAsync("beta", bDir);

            var aPath = Path.Combine(_vaultDir, "an.md");
            var bPath = Path.Combine(bDir, "bn.md");
            await File.WriteAllTextAsync(aPath, "shared keyword zebrafinch");
            await File.WriteAllTextAsync(bPath, "shared keyword zebrafinch");
            await _fx.UpsertAsync(aId, [Doc(aPath, "shared keyword zebrafinch")]);
            await _fx.UpsertAsync(bId, [Doc(bPath, "shared keyword zebrafinch")]);

            var aHits = await _ctx.ContextAsync("zebrafinch", vaultId: aId);

            Assert.Single(aHits);
            Assert.Equal("an.md", aHits[0].RelativePath);
        }
        finally { try { Directory.Delete(bDir, recursive: true); } catch { } }
    }

    private static NoteDocument Doc(string path, string body) => new(
        Path: path,
        Frontmatter: new Dictionary<string, object>(),
        Tags: [],
        OutgoingLinks: [],
        Content: body,
        RawContent: body,
        Mtime: 1,
        Size: body.Length);
}
