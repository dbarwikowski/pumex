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
    public async Task ResolveNotePathAsync_accepts_forward_slashes_in_absolute_windows_path()
    {
        // Bash-on-Windows users type C:/foo/bar.md to dodge backslash escaping;
        // .NET's Path.IsPathFullyQualified + GetFullPath accept both directions
        // and canonicalise to the platform separator.
        if (!OperatingSystem.IsWindows()) return;
        using var fx = new TestDbFixture();
        var raw = "C:/some/path/note.md";

        var resolved = await IpcRequestExtensions.ResolveNotePathAsync(raw, vault: null, fx.Notes);

        Assert.Equal(@"C:\some\path\note.md", resolved);
    }

    [Fact]
    public async Task ResolveNotePathAsync_accepts_forward_slashes_in_vault_relative_path()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fx = new TestDbFixture();
        var vault = new VaultRecord(Id: 1, Name: "v", Path: @"C:\vault");

        var resolved = await IpcRequestExtensions.ResolveNotePathAsync("sub/note.md", vault, fx.Notes);

        Assert.Equal(@"C:\vault\sub\note.md", resolved);
    }

    [Fact]
    public async Task ResolveNotePathAsync_returns_canonical_absolute_path_unchanged()
    {
        using var fx = new TestDbFixture();
        var raw = OperatingSystem.IsWindows() ? @"C:\foo\..\bar\note.md" : "/foo/../bar/note.md";
        var expected = Path.GetFullPath(raw);

        var resolved = await IpcRequestExtensions.ResolveNotePathAsync(raw, vault: null, fx.Notes);

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public async Task ResolveNotePathAsync_appends_md_when_path_has_separator_but_no_extension()
    {
        using var fx = new TestDbFixture();
        var vault = new VaultRecord(Id: 1, Name: "v", Path: OperatingSystem.IsWindows() ? @"C:\vault" : "/vault");

        var resolved = await IpcRequestExtensions.ResolveNotePathAsync("wiki/index", vault, fx.Notes);

        var expected = Path.GetFullPath(Path.Combine(vault.Path, "wiki/index.md"));
        Assert.Equal(expected, resolved);
    }

    [Fact]
    public async Task ResolveNotePathAsync_joins_relative_against_vault_root_when_vault_in_scope()
    {
        using var fx = new TestDbFixture();
        var vault = new VaultRecord(Id: 1, Name: "v", Path: OperatingSystem.IsWindows() ? @"C:\vault" : "/vault");

        var resolved = await IpcRequestExtensions.ResolveNotePathAsync("sub/note.md", vault, fx.Notes);

        var expected = Path.GetFullPath(Path.Combine(vault.Path, "sub/note.md"));
        Assert.Equal(expected, resolved);
    }

    [Fact]
    public async Task ResolveNotePathAsync_bare_name_resolves_via_index_when_unique()
    {
        using var fx = new TestDbFixture();
        var vaultId = await fx.Vaults.AddVaultAsync("alpha", "/alpha");
        var vault = (await fx.Vaults.GetVaultByPathAsync("/alpha"))!;
        await fx.UpsertAsync(vaultId, [Note("/alpha/today.md")]);

        var resolved = await IpcRequestExtensions.ResolveNotePathAsync("today", vault, fx.Notes);

        Assert.Equal("/alpha/today.md", resolved);
    }

    [Fact]
    public async Task ResolveNotePathAsync_strips_md_suffix_before_lookup()
    {
        using var fx = new TestDbFixture();
        var vaultId = await fx.Vaults.AddVaultAsync("v", "/v");
        var vault = (await fx.Vaults.GetVaultByPathAsync("/v"))!;
        await fx.UpsertAsync(vaultId, [Note("/v/today.md")]);

        var resolved = await IpcRequestExtensions.ResolveNotePathAsync("today.md", vault, fx.Notes);

        Assert.Equal("/v/today.md", resolved);
    }

    [Fact]
    public async Task ResolveNotePathAsync_throws_when_name_has_no_match()
    {
        using var fx = new TestDbFixture();
        await fx.Vaults.AddVaultAsync("v", "/v");
        var vault = (await fx.Vaults.GetVaultByPathAsync("/v"))!;

        await Assert.ThrowsAsync<FileNotFoundException>(
            async () => await IpcRequestExtensions.ResolveNotePathAsync("ghost", vault, fx.Notes));
    }

    [Fact]
    public async Task ResolveNotePathAsync_throws_when_name_is_ambiguous()
    {
        using var fx = new TestDbFixture();
        var vaultId = await fx.Vaults.AddVaultAsync("v", "/v");
        var vault = (await fx.Vaults.GetVaultByPathAsync("/v"))!;
        await fx.UpsertAsync(vaultId, [
            Note("/v/a/shared.md"),
            Note("/v/b/shared.md"),
        ]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await IpcRequestExtensions.ResolveNotePathAsync("shared", vault, fx.Notes));
    }

    [Fact]
    public async Task ResolveNotePathAsync_throws_when_bare_name_has_no_vault_in_scope()
    {
        using var fx = new TestDbFixture();

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await IpcRequestExtensions.ResolveNotePathAsync("today", vault: null, fx.Notes));
    }

    [Fact]
    public async Task ResolveNotePathAsync_create_mode_generates_path_inside_vault_root_without_db_lookup()
    {
        using var fx = new TestDbFixture();
        var vault = new VaultRecord(Id: 1, Name: "v", Path: OperatingSystem.IsWindows() ? @"C:\v" : "/v");

        var resolved = await IpcRequestExtensions.ResolveNotePathAsync("brand-new", vault, fx.Notes, NoteResolutionMode.Create);

        Assert.Equal(Path.GetFullPath(Path.Combine(vault.Path, "brand-new.md")), resolved);
    }

    private static NoteDocument Note(string path) => new(
        Path: path,
        Frontmatter: new Dictionary<string, object>(),
        Tags: [],
        OutgoingLinks: [],
        Content: "",
        RawContent: "",
        Mtime: 1,
        Size: 0);

    [Fact]
    public async Task ResolveVaultAsync_returns_null_when_no_vault_args_supplied()
    {
        using var fx = new TestDbFixture();
        var r = Req();

        var vault = await r.ResolveVaultAsync(fx.Vaults);

        Assert.Null(vault);
    }

    [Fact]
    public async Task ResolveVaultAsync_throws_when_named_vault_is_unknown()
    {
        using var fx = new TestDbFixture();
        var r = Req(("vault", "ghost"));

        await Assert.ThrowsAsync<ArgumentException>(async () => await r.ResolveVaultAsync(fx.Vaults));
    }

    [Fact]
    public async Task ResolveVaultAsync_returns_vault_when_name_matches()
    {
        using var fx = new TestDbFixture();
        await fx.Vaults.AddVaultAsync("alpha", "/alpha");
        var r = Req(("vault", "alpha"));

        var vault = await r.ResolveVaultAsync(fx.Vaults);

        Assert.NotNull(vault);
        Assert.Equal("alpha", vault!.Name);
    }

    [Fact]
    public async Task ResolveVaultAsync_throws_when_path_is_unregistered_and_not_marked_optional()
    {
        using var fx = new TestDbFixture();
        var r = Req(("vaultPath", Path.GetFullPath("/no-such-place")));

        await Assert.ThrowsAsync<ArgumentException>(async () => await r.ResolveVaultAsync(fx.Vaults));
    }

    [Fact]
    public async Task ResolveVaultAsync_falls_back_to_global_when_vaultOptional_is_set()
    {
        using var fx = new TestDbFixture();
        var r = Req(("vaultPath", Path.GetFullPath("/no-such-place")), ("vaultOptional", "1"));

        var vault = await r.ResolveVaultAsync(fx.Vaults);

        Assert.Null(vault);
    }
}
