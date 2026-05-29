using Pumex.Contracts;

namespace Pumex.Daemon.Tests;

public class FormatFilterTests : IDisposable
{
    private readonly TestDbFixture _fx = new();
    private long _vaultId;

    private async Task<long> VaultAsync()
    {
        if (_vaultId != 0) return _vaultId;
        var root = Path.Combine(Path.GetTempPath(), "fmt-vault-" + Guid.NewGuid().ToString("N"));
        await _fx.Vaults.AddVaultAsync("fmt", root);
        _vaultId = (await _fx.Vaults.GetVaultByPathAsync(root))!.Id;
        return _vaultId;
    }

    private static NoteDocument Doc(string path, string body) =>
        new(path, new(), [], [], body, body, 0, body.Length);

    private async Task SeedAsync(long vaultId) => await _fx.UpsertAsync(vaultId,
    [
        Doc(Path.Combine("v", "alpha.md"), "alpha body"),
        Doc(Path.Combine("v", "beta.csv"), "beta body"),
        Doc(Path.Combine("v", "gamma.json"), "gamma body"),
    ]);

    [Fact]
    public async Task Upsert_records_the_file_format()
    {
        var vault = await VaultAsync();
        await SeedAsync(vault);

        var all = await _fx.Notes.ListNotesAsync(vault);
        Assert.Equal("md", all.Single(n => n.Name == "alpha").Format);
        Assert.Equal("csv", all.Single(n => n.Name == "beta").Format);
        Assert.Equal("json", all.Single(n => n.Name == "gamma").Format);
    }

    [Fact]
    public async Task List_filters_by_format()
    {
        var vault = await VaultAsync();
        await SeedAsync(vault);

        var csvOnly = await _fx.Notes.ListNotesAsync(vault, ["csv"]);
        Assert.Equal("beta", Assert.Single(csvOnly).Name);

        var two = await _fx.Notes.ListNotesAsync(vault, ["csv", "json"]);
        Assert.Equal(2, two.Count);
    }

    [Fact]
    public async Task Search_filters_by_format()
    {
        var vault = await VaultAsync();
        await SeedAsync(vault);

        var mdHits = await _fx.Search.SearchAsync("body", vaultId: vault, formats: ["md"]);
        Assert.Equal("alpha", Assert.Single(mdHits).Name);
    }

    [Fact]
    public async Task Bare_name_resolution_is_markdown_only()
    {
        var vault = await VaultAsync();
        await _fx.UpsertAsync(vault,
        [
            Doc(Path.Combine("v", "data.md"), "the markdown one"),
            Doc(Path.Combine("v", "data.csv"), "the csv one"),
        ]);

        // Bare name → Markdown only.
        var byName = await _fx.Notes.GetNotePathsByNameAsync(vault, "data");
        Assert.EndsWith("data.md", Assert.Single(byName));

        // Explicit filename → the exact file.
        var byFile = await _fx.Notes.GetNotePathsByFileNameAsync(vault, "data.csv");
        Assert.EndsWith("data.csv", Assert.Single(byFile));
    }

    public void Dispose() => _fx.Dispose();
}
