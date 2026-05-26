using Pumex.Contracts;
using Pumex.Daemon.Ipc;

namespace Pumex.Daemon.Tests;

/// <summary>
/// Creates a fresh <see cref="IndexDbContext"/>, applies the schema, and wires
/// up all repositories so individual test classes only need to reference the
/// repos they care about. Exposes a <see cref="UpsertAsync"/> convenience method
/// that handles the gate + transaction + cache-flush dance.
/// </summary>
internal sealed class TestDbFixture : IDisposable
{
    private readonly string _dir;

    public IndexDbContext Context { get; }
    public IVaultRepository Vaults { get; }
    public INoteRepository Notes { get; }
    public ISearchRepository Search { get; }
    public ILinkRepository Links { get; }
    public IInlineIndex InlineIndex { get; }

    public TestDbFixture()
    {
        _dir = Path.Combine(Path.GetTempPath(), "pumex-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        Context = new IndexDbContext(Path.Combine(_dir, "index.db"));
        new IndexSchema(Context).Apply();
        Notes = new NoteRepository(Context);
        Links = new LinkRepository(Context);
        Vaults = new VaultRepository(Context, Notes);
        Search = new SearchRepository(Context);
        InlineIndex = new Pumex.Daemon.Ipc.InlineIndex(Context, Notes, Links, new NoteParser());
    }

    /// <summary>
    /// Convenience wrapper that performs a full upsert (notes + tags + properties
    /// + FTS + links) inside a single transaction, mirrors what
    /// <see cref="IndexingService"/> does at runtime.
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

    public void Dispose()
    {
        Context.Dispose();
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* test cleanup is best-effort */ }
    }
}
