using Pumex.Cli;

namespace Pumex.Cli.Tests;

public class VaultScopeTests
{
    [Fact]
    public void ApplyTo_empty_dictionary_when_scope_is_global()
    {
        var args = new Dictionary<string, string>();

        VaultScope.Global.ApplyTo(args);

        Assert.Empty(args);
    }

    [Fact]
    public void ApplyTo_named_explicit_scope_sets_only_vault_arg()
    {
        var args = new Dictionary<string, string>();
        var scope = new VaultScope(Name: "alpha", Path: null, IsExplicit: true);

        scope.ApplyTo(args);

        Assert.Equal("alpha", args["vault"]);
        Assert.False(args.ContainsKey("vaultPath"));
        Assert.False(args.ContainsKey("vaultOptional"));
    }

    [Fact]
    public void ApplyTo_explicit_path_scope_does_not_set_vaultOptional()
    {
        var args = new Dictionary<string, string>();
        var scope = new VaultScope(Name: null, Path: "/explicit", IsExplicit: true);

        scope.ApplyTo(args);

        Assert.Equal("/explicit", args["vaultPath"]);
        Assert.False(args.ContainsKey("vaultOptional"));
    }

    [Fact]
    public void ApplyTo_auto_discovered_path_marks_vaultOptional_so_daemon_can_fall_back()
    {
        var args = new Dictionary<string, string>();
        var scope = new VaultScope(Name: null, Path: "/discovered", IsExplicit: false);

        scope.ApplyTo(args);

        Assert.Equal("/discovered", args["vaultPath"]);
        Assert.Equal("1", args["vaultOptional"]);
    }

    [Fact]
    public void RelativeRoot_is_only_set_for_explicit_scope()
    {
        Assert.Null(new VaultScope("v", null,            IsExplicit: true).RelativeRoot);
        Assert.Equal("/x", new VaultScope(null, "/x",     IsExplicit: true).RelativeRoot);
        Assert.Null(new VaultScope(null, "/discovered",   IsExplicit: false).RelativeRoot);
    }

    [Fact]
    public void ResolvePath_returns_canonical_absolute_path_unchanged()
    {
        var raw = OperatingSystem.IsWindows() ? @"C:\foo\..\bar\note.md" : "/foo/../bar/note.md";
        var scope = new VaultScope(Name: "v", Path: null, IsExplicit: true);

        var resolved = VaultArgs.ResolvePath(scope, raw);

        Assert.Equal(Path.GetFullPath(raw), resolved);
    }

    [Fact]
    public void ResolvePath_leaves_relative_alone_when_only_vault_name_was_given()
    {
        // The CLI doesn't know the named vault's root — defer to the daemon.
        var scope = new VaultScope(Name: "alpha", Path: null, IsExplicit: true);

        var resolved = VaultArgs.ResolvePath(scope, "sub/note.md");

        Assert.Equal("sub/note.md", resolved);
    }

    [Fact]
    public void ResolvePath_joins_relative_against_explicit_path()
    {
        var explicitRoot = OperatingSystem.IsWindows() ? @"C:\vault" : "/vault";
        var scope = new VaultScope(Name: null, Path: explicitRoot, IsExplicit: true);

        var resolved = VaultArgs.ResolvePath(scope, "sub/note.md");

        Assert.Equal(Path.GetFullPath(Path.Combine(explicitRoot, "sub/note.md")), resolved);
    }

    [Fact]
    public void ResolvePath_passes_bare_names_through_unchanged()
    {
        // No directory separator → daemon-side lookup by name. CLI must not
        // canonicalise CWD-relative or the daemon never sees the bare token.
        var scope = new VaultScope(Name: null, Path: "/discovered", IsExplicit: false);

        Assert.Equal("today", VaultArgs.ResolvePath(scope, "today"));
        Assert.Equal("today.md", VaultArgs.ResolvePath(scope, "today.md"));
    }

    [Fact]
    public void ResolvePath_resolves_path_with_separator_against_cwd_for_auto_discovered_scope()
    {
        // Auto-discovered scope deliberately preserves CWD-relative behaviour
        // for path-shaped inputs. (Bare names go through a separate branch
        // and are passed through to the daemon for index lookup.)
        var scope = new VaultScope(Name: null, Path: "/discovered", IsExplicit: false);

        var resolved = VaultArgs.ResolvePath(scope, "sub/note.md");

        Assert.Equal(Path.GetFullPath("sub/note.md"), resolved);
    }
}
