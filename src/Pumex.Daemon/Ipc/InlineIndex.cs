namespace Pumex.Daemon.Ipc;

/// <summary>
/// Reindexes a single note synchronously after a write, so callers don't race
/// the watcher's 200 ms debounce. The watcher will still fire on the same file
/// shortly after, but the mtime-match short-circuit makes that a no-op.
/// </summary>
public interface IInlineIndex
{
    Task UpsertAsync(long vaultId, string path);
    Task DeleteAsync(string path);
}

public class InlineIndex(
    IndexDbContext context,
    INoteRepository noteRepo,
    ILinkRepository linkRepo,
    NoteParser parser) : IInlineIndex
{
    public async Task UpsertAsync(long vaultId, string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            var doc = parser.Parse(path);
            using var gate = await context.AcquireAsync();
            using var tx = context.BeginTransaction();
            try
            {
                var result = await noteRepo.UpsertCoreAsync(tx, vaultId, [doc]);
                await linkRepo.DeleteLinksForNotesAsync(tx, result.Entries.Select(e => e.Id).ToList());
                await linkRepo.InsertLinksAsync(tx, result.Links);
                tx.Commit();
                noteRepo.UpdateCacheUnsafe(result.Entries);
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
        catch
        {
            // Inline indexing is best-effort — don't fail the write because
            // the parse or upsert hiccupped. The watcher will retry.
        }
    }

    public async Task DeleteAsync(string path)
    {
        try { await noteRepo.DeleteNoteAsync(path); }
        catch { /* watcher will catch up; don't fail the delete */ }
    }
}
