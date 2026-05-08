using Pumex.Contracts;

namespace Pumex.Daemon.IntegrationTests.Helpers;

/// <summary>
/// One-stop scratch environment: a temp directory that doubles as a vault root
/// plus its own dedicated <see cref="IndexDb"/> on disk. Tests get a registered
/// vault to work against and clean up everything on dispose.
/// </summary>
internal sealed class TestVault : IDisposable
{
    public string Root { get; }
    public string DbPath { get; }
    public IndexDb Db { get; }
    public VaultRecord Vault { get; private set; } = null!;

    private TestVault(string root, string dbPath)
    {
        Root = root;
        DbPath = dbPath;
        Db = new IndexDb(dbPath);
    }

    public static async Task<TestVault> CreateAsync(string vaultName = "test-vault")
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "pumex-int-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        var root = Path.Combine(sandbox, "vault");
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(sandbox, "index.db");

        var fixture = new TestVault(root, dbPath);
        await fixture.Db.AddVaultAsync(vaultName, root);
        fixture.Vault = (await fixture.Db.GetVaultByPathAsync(root))!;
        return fixture;
    }

    public string WriteNote(string relativePath, string content)
    {
        var full = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        Db.Dispose();
        try
        {
            var sandbox = Directory.GetParent(Root)!.FullName;
            Directory.Delete(sandbox, recursive: true);
        }
        catch { /* test cleanup is best-effort */ }
    }
}
