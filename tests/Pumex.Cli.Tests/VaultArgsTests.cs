using Pumex.Cli;
using Pumex.Contracts;

namespace Pumex.Cli.Tests;

public class VaultArgsTests : IDisposable
{
    private readonly string _tempRoot;

    public VaultArgsTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "pumex-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); }
        catch { /* test cleanup is best-effort */ }
    }

    [Fact]
    public void Extract_with_no_flags_and_cwd_outside_any_vault_yields_global_scope()
    {
        // CWD is a synthetic non-existent path under the filesystem root so the
        // walk-up doesn't traverse the user profile's ~/.pumex data dir.
        var driveRoot = OperatingSystem.IsWindows()
            ? Path.GetPathRoot(Path.GetTempPath())!
            : "/";
        var isolated = Path.Combine(driveRoot, "pumex-isolated-" + Guid.NewGuid().ToString("N"));

        var (scope, rest) = VaultArgs.Extract(new[] { "search", "foo" }, cwd: isolated);

        Assert.False(scope.HasScope);
        Assert.False(scope.IsExplicit);
        Assert.Equal(new[] { "search", "foo" }, rest);
    }

    [Fact]
    public void Extract_auto_discovers_vault_when_cwd_is_inside_one()
    {
        var vault = Path.Combine(_tempRoot, "vault");
        Directory.CreateDirectory(Path.Combine(vault, PumexPaths.VaultMarkerDir));

        var (scope, _) = VaultArgs.Extract(new[] { "tags" }, cwd: vault);

        Assert.True(scope.HasScope);
        Assert.False(scope.IsExplicit);
        Assert.Equal(vault, scope.Path);
    }

    [Fact]
    public void Extract_with_dash_dash_vault_picks_named_explicit_scope()
    {
        var (scope, rest) = VaultArgs.Extract(new[] { "search", "foo", "--vault", "my-vault" }, cwd: _tempRoot);

        Assert.True(scope.IsExplicit);
        Assert.Equal("my-vault", scope.Name);
        Assert.Null(scope.Path);
        Assert.Equal(new[] { "search", "foo" }, rest);
    }

    [Fact]
    public void Extract_with_dash_dash_vault_path_picks_explicit_path_scope()
    {
        var explicitPath = Path.Combine(_tempRoot, "elsewhere");
        Directory.CreateDirectory(explicitPath);

        var (scope, rest) = VaultArgs.Extract(new[] { "tags", "--vault-path", explicitPath }, cwd: _tempRoot);

        Assert.True(scope.IsExplicit);
        Assert.Equal(Path.GetFullPath(explicitPath), scope.Path);
        Assert.Null(scope.Name);
        Assert.Equal(new[] { "tags" }, rest);
    }

    [Fact]
    public void Extract_with_dash_dash_all_overrides_auto_discovery()
    {
        var vault = Path.Combine(_tempRoot, "vault");
        Directory.CreateDirectory(Path.Combine(vault, PumexPaths.VaultMarkerDir));

        var (scope, rest) = VaultArgs.Extract(new[] { "tags", "--all" }, cwd: vault);

        Assert.False(scope.HasScope);
        Assert.Equal(new[] { "tags" }, rest);
    }

    [Fact]
    public void Extract_strips_consumed_flags_from_remaining_args()
    {
        var (_, rest) = VaultArgs.Extract(
            new[] { "search", "foo", "--vault", "v", "--limit", "10" },
            cwd: _tempRoot);

        Assert.Equal(new[] { "search", "foo", "--limit", "10" }, rest);
    }
}
