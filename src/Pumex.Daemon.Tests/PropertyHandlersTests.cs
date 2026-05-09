using Pumex.Contracts;
using Pumex.Daemon.Ipc;

namespace Pumex.Daemon.Tests;

public class PropertyHandlersTests : IDisposable
{
    private readonly TempVault _vault = new();
    private readonly string _dbPath;
    private readonly IndexDb _db;

    public PropertyHandlersTests()
    {
        _dbPath = Path.Combine(_vault.Path, "index.db");
        _db = new IndexDb(_dbPath);
    }

    public void Dispose()
    {
        _db.Dispose();
        _vault.Dispose();
    }

    private static IpcRequest Req(string command, params (string Key, string Value)[] args)
        => new(command, args.ToDictionary(a => a.Key, a => a.Value));

    [Fact]
    public async Task PropertySet_adds_frontmatter_when_note_has_none()
    {
        var path = _vault.WriteNote("plain.md", "# Plain\n\nbody only\n");
        var handler = new PropertySetHandler(_db, new NoteParser());

        await handler.HandleAsync(Req("property:set",
            ("path", path), ("key", "status"), ("value", "draft")), CancellationToken.None);

        var content = File.ReadAllText(path).Replace("\r\n", "\n");
        Assert.StartsWith("---\n", content);
        Assert.Contains("status: draft", content);
        Assert.Contains("# Plain", content);
        Assert.Contains("body only", content);
    }

    [Fact]
    public async Task PropertySet_updates_existing_key_in_place()
    {
        var path = _vault.WriteNote("note.md", "---\nstatus: draft\ntitle: Hello\n---\n\nbody\n");
        var handler = new PropertySetHandler(_db, new NoteParser());

        await handler.HandleAsync(Req("property:set",
            ("path", path), ("key", "status"), ("value", "published")), CancellationToken.None);

        var content = File.ReadAllText(path).Replace("\r\n", "\n");
        Assert.Contains("status: published", content);
        Assert.DoesNotContain("status: draft", content);
        Assert.Contains("title: Hello", content);
        Assert.Contains("body", content);
    }

    [Fact]
    public async Task PropertySet_adds_new_key_to_existing_frontmatter()
    {
        var path = _vault.WriteNote("note.md", "---\ntitle: Hello\n---\n\nbody\n");
        var handler = new PropertySetHandler(_db, new NoteParser());

        await handler.HandleAsync(Req("property:set",
            ("path", path), ("key", "priority"), ("value", "high")), CancellationToken.None);

        var content = File.ReadAllText(path).Replace("\r\n", "\n");
        Assert.Contains("title: Hello", content);
        Assert.Contains("priority: high", content);
    }

    [Fact]
    public async Task PropertySet_throws_on_missing_file()
    {
        var handler = new PropertySetHandler(_db, new NoteParser());
        var ghostPath = Path.Combine(_vault.Path, "ghost.md");

        await Assert.ThrowsAsync<FileNotFoundException>(async () => await handler.HandleAsync(
            Req("property:set", ("path", ghostPath), ("key", "k"), ("value", "v")),
            CancellationToken.None));
    }

    [Fact]
    public async Task PropertyList_returns_frontmatter_after_indexing()
    {
        var vaultId = await _db.AddVaultAsync("v", _vault.Path);
        var path = _vault.WriteNote("n.md", "---\ntitle: Hi\nstatus: live\n---\n\nbody\n");

        // Simulate the indexer having seen this note.
        var doc = new NoteParser().Parse(path);
        await _db.UpsertNotesAsync(vaultId, [doc]);

        var handler = new PropertyListHandler(_db);
        var result = (List<PropertyEntry>)(await handler.HandleAsync(
            Req("property:list", ("path", path)), CancellationToken.None))!;

        Assert.Equal(2, result.Count);
        Assert.Contains(result, p => p.Key == "title"  && p.Value == "Hi");
        Assert.Contains(result, p => p.Key == "status" && p.Value == "live");
    }

    [Fact]
    public async Task PropertyGet_throws_when_key_is_absent()
    {
        var vaultId = await _db.AddVaultAsync("v", _vault.Path);
        var path = _vault.WriteNote("n.md", "---\ntitle: Hi\n---\n\nbody\n");
        var doc = new NoteParser().Parse(path);
        await _db.UpsertNotesAsync(vaultId, [doc]);

        var handler = new PropertyGetHandler(_db);

        await Assert.ThrowsAsync<KeyNotFoundException>(async () => await handler.HandleAsync(
            Req("property:get", ("path", path), ("key", "missing")),
            CancellationToken.None));
    }
}
