using Pumex.Contracts;

namespace Pumex.Contracts.Tests;

public class PumexPathsTests : IDisposable
{
    private readonly string _tempRoot;

    public PumexPathsTests()
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
    public void FindVaultRoot_returns_starting_dir_when_marker_present()
    {
        var vault = Path.Combine(_tempRoot, "vault");
        Directory.CreateDirectory(Path.Combine(vault, PumexPaths.VaultMarkerDir));

        var found = PumexPaths.FindVaultRoot(vault);

        Assert.Equal(vault, found);
    }

    [Fact]
    public void FindVaultRoot_walks_up_to_find_marker_in_parent()
    {
        var vault = Path.Combine(_tempRoot, "vault");
        var sub = Path.Combine(vault, "sub", "deep");
        Directory.CreateDirectory(sub);
        Directory.CreateDirectory(Path.Combine(vault, PumexPaths.VaultMarkerDir));

        var found = PumexPaths.FindVaultRoot(sub);

        Assert.Equal(vault, found);
    }

    [Fact]
    public void FindVaultRoot_returns_null_when_no_marker_in_ancestry()
    {
        // Start from a synthetic path under the filesystem root so the walk-up
        // doesn't traverse the user profile (where the daemon's own ~/.pumex
        // data dir would otherwise be misread as a vault marker).
        var driveRoot = OperatingSystem.IsWindows()
            ? Path.GetPathRoot(Path.GetTempPath())!
            : "/";
        var lonely = Path.Combine(driveRoot, "pumex-isolated-" + Guid.NewGuid().ToString("N"), "child");

        var found = PumexPaths.FindVaultRoot(lonely);

        Assert.Null(found);
    }

    [Fact]
    public void FindVaultRoot_returns_closest_marker_when_vaults_are_nested()
    {
        var outer = Path.Combine(_tempRoot, "outer");
        var inner = Path.Combine(outer, "inner");
        Directory.CreateDirectory(Path.Combine(outer, PumexPaths.VaultMarkerDir));
        Directory.CreateDirectory(Path.Combine(inner, PumexPaths.VaultMarkerDir));

        var found = PumexPaths.FindVaultRoot(Path.Combine(inner, "any", "sub"));

        Assert.Equal(inner, found);
    }
}
