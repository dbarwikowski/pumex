namespace Pumex.Daemon.Ipc;

/// <summary>
/// Reindexes a single note synchronously after a write, so callers don't race
/// the watcher's 200 ms debounce. The watcher will still fire on the same file
/// shortly after, but the mtime-match short-circuit makes that a no-op.
/// </summary>
internal static class InlineIndex
{
    public static async Task UpsertAsync(IndexDb db, NoteParser parser, long vaultId, string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            var doc = parser.Parse(path);
            await db.UpsertNotesAsync(vaultId, [doc]);
        }
        catch
        {
            // Inline indexing is best-effort — don't fail the write because
            // the parse or upsert hiccupped. The watcher will retry.
        }
    }

    public static async Task DeleteAsync(IndexDb db, string path)
    {
        try { await db.DeleteNoteAsync(path); }
        catch { /* watcher will catch up; don't fail the delete */ }
    }
}
