using Pumex.Contracts;
using Pumex.Daemon.Ipc;

namespace Pumex.Daemon.Tests;

public class IpcRequestExtensionsTests
{
    private static IpcRequest Req(params (string Key, string Value)[] args)
        => new("test", args.ToDictionary(a => a.Key, a => a.Value));

    [Fact]
    public void Require_returns_value_when_present()
    {
        var r = Req(("path", "/foo.md"));
        Assert.Equal("/foo.md", r.Require("path"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Require_throws_when_missing_or_blank(string value)
    {
        var r = string.IsNullOrEmpty(value) ? Req() : Req(("path", value));
        Assert.Throws<ArgumentException>(() => r.Require("path"));
    }

    [Fact]
    public void Optional_returns_null_when_absent()
    {
        Assert.Null(Req().Optional("path"));
    }

    [Theory]
    [InlineData("1",     true)]
    [InlineData("true",  true)]
    [InlineData("yes",   true)]
    [InlineData("0",     false)]
    [InlineData("false", false)]
    [InlineData("",      false)]
    public void Flag_recognizes_truthy_values(string value, bool expected)
    {
        var r = Req(("inline", value));
        Assert.Equal(expected, r.Flag("inline"));
    }

    [Fact]
    public void Flag_returns_false_when_arg_is_missing()
    {
        Assert.False(Req().Flag("inline"));
    }

    [Fact]
    public void ResolveNotePath_returns_canonical_absolute_path_unchanged()
    {
        // Pick a path the OS can canonicalise without touching disk.
        var raw = OperatingSystem.IsWindows() ? @"C:\foo\..\bar\note.md" : "/foo/../bar/note.md";
        var expected = Path.GetFullPath(raw);

        var resolved = IpcRequestExtensions.ResolveNotePath(raw, vault: null);

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void ResolveNotePath_joins_relative_against_vault_root_when_vault_in_scope()
    {
        var vault = new VaultRecord(Id: 1, Name: "v", Path: OperatingSystem.IsWindows() ? @"C:\vault" : "/vault");

        var resolved = IpcRequestExtensions.ResolveNotePath("sub/note.md", vault);

        var expected = Path.GetFullPath(Path.Combine(vault.Path, "sub/note.md"));
        Assert.Equal(expected, resolved);
    }

    [Fact]
    public async Task ResolveVaultAsync_returns_null_when_no_vault_args_supplied()
    {
        using var fixture = new IndexDbFixture();
        var r = Req();

        var vault = await r.ResolveVaultAsync(fixture.Db);

        Assert.Null(vault);
    }

    [Fact]
    public async Task ResolveVaultAsync_throws_when_named_vault_is_unknown()
    {
        using var fixture = new IndexDbFixture();
        var r = Req(("vault", "ghost"));

        await Assert.ThrowsAsync<ArgumentException>(async () => await r.ResolveVaultAsync(fixture.Db));
    }

    [Fact]
    public async Task ResolveVaultAsync_returns_vault_when_name_matches()
    {
        using var fixture = new IndexDbFixture();
        await fixture.Db.AddVaultAsync("alpha", "/alpha");
        var r = Req(("vault", "alpha"));

        var vault = await r.ResolveVaultAsync(fixture.Db);

        Assert.NotNull(vault);
        Assert.Equal("alpha", vault!.Name);
    }

    [Fact]
    public async Task ResolveVaultAsync_throws_when_path_is_unregistered_and_not_marked_optional()
    {
        using var fixture = new IndexDbFixture();
        var r = Req(("vaultPath", Path.GetFullPath("/no-such-place")));

        await Assert.ThrowsAsync<ArgumentException>(async () => await r.ResolveVaultAsync(fixture.Db));
    }

    [Fact]
    public async Task ResolveVaultAsync_falls_back_to_global_when_vaultOptional_is_set()
    {
        using var fixture = new IndexDbFixture();
        var r = Req(("vaultPath", Path.GetFullPath("/no-such-place")), ("vaultOptional", "1"));

        var vault = await r.ResolveVaultAsync(fixture.Db);

        Assert.Null(vault);
    }

    private sealed class IndexDbFixture : IDisposable
    {
        private readonly string _root;
        public IndexDb Db { get; }

        public IndexDbFixture()
        {
            _root = Path.Combine(Path.GetTempPath(), "pumex-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            Db = new IndexDb(Path.Combine(_root, "index.db"));
        }

        public void Dispose()
        {
            Db.Dispose();
            try { Directory.Delete(_root, recursive: true); }
            catch { /* test cleanup is best-effort */ }
        }
    }
}
