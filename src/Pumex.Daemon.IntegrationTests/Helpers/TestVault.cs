using Pumex.Contracts;

namespace Pumex.Daemon.IntegrationTests.Helpers;

/// <summary>
/// One-stop scratch environment: a temp directory that doubles as a vault root
/// plus its own dedicated database and all repositories wired up. Tests get a
/// registered vault to work against and clean up everything on dispose.
/// </summary>
internal sealed class TestVault : IDisposable
{
    public string Root { get; }
    public string DbPath { get; }
    public IndexDbContext Context { get; }
    public IVaultRepository Vaults { get; }
    public INoteRepository Notes { get; }
    public ISearchRepository Search { get; }
    public ILinkRepository Links { get; }
    public VaultRecord Vault { get; private set; } = null!;

    private TestVault(string root, string dbPath)
    {
        Root = root;
        DbPath = dbPath;
        Context = new IndexDbContext(dbPath);
        new IndexSchema(Context).Apply();
        Notes = new NoteRepository(Context);
        Links = new LinkRepository(Context);
        Vaults = new VaultRepository(Context, Notes);
        Search = new SearchRepository(Context);
    }

    public static async Task<TestVault> CreateAsync(string vaultName = "test-vault")
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "pumex-int-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        var root = Path.Combine(sandbox, "vault");
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(sandbox, "index.db");

        var fixture = new TestVault(root, dbPath);
        await fixture.Vaults.AddVaultAsync(vaultName, root);
        fixture.Vault = (await fixture.Vaults.GetVaultByPathAsync(root))!;
        return fixture;
    }

    /// <summary>
    /// Convenience wrapper: upserts a batch of notes (including links) inside a
    /// single transaction, mirroring what <c>IndexingService</c> does.
    /// </summary>
    public async Task UpsertAsync(long vaultId, IEnumerable<NoteDocument> notes)
    {
        using var gate = await Context.AcquireAsync();
        using var tx = Context.BeginTransaction();
        try
        {
            var result = await Notes.UpsertCoreAsync(tx, vaultId, notes);
            await Links.DeleteLinksForNotesAsync(tx, result.Entries.Select(e => e.Id).ToList());
            await Links.InsertLinksAsync(tx, result.Links);
            tx.Commit();
            Notes.UpdateCacheUnsafe(result.Entries);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
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
        Context.Dispose();
        try
        {
            var sandbox = Directory.GetParent(Root)!.FullName;
            Directory.Delete(sandbox, recursive: true);
        }
        catch { /* test cleanup is best-effort */ }
    }
}
