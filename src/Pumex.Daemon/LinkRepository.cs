using Microsoft.Data.Sqlite;
using Pumex.Contracts;

namespace Pumex.Daemon;

public record UnresolvedLink(long SourceId, string SourcePath, string TargetText);

public class LinkRepository(IndexDbContext context) : ILinkRepository
{
    public async Task<List<string>> GetBacklinksAsync(string path, long? vaultId = null)
    {
        using var _ = await context.AcquireAsync();
        var result = new List<string>();
        using var cmd = vaultId is null
            ? context.Command("""
                SELECT DISTINCT n.path
                FROM links l
                JOIN notes n ON n.id = l.source_id
                JOIN notes t ON t.id = l.resolved_id
                WHERE t.path = @path
                """, ("@path", path))
            : context.Command("""
                SELECT DISTINCT n.path
                FROM links l
                JOIN notes n ON n.id = l.source_id
                JOIN notes t ON t.id = l.resolved_id
                WHERE t.path = @path AND n.vault_id = @vaultId
                """, ("@path", path), ("@vaultId", vaultId.Value));
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0));
        return result;
    }

    public async Task SetLinkResolutionAsync(long sourceId, string targetText, long? resolvedId)
    {
        using var _ = await context.AcquireAsync();
        await context.ExecuteAsync("""
            UPDATE links SET resolved_id = @resolvedId
            WHERE source_id = @sourceId AND target_path = @targetText
            """,
            ("@resolvedId", (object?)resolvedId ?? DBNull.Value),
            ("@sourceId", sourceId),
            ("@targetText", targetText));
    }

    public async Task<List<UnresolvedLink>> GetUnresolvedLinksAsync(long vaultId)
    {
        using var _ = await context.AcquireAsync();
        var result = new List<UnresolvedLink>();
        using var cmd = context.Command("""
            SELECT l.source_id, n.path, l.target_path
            FROM links l
            JOIN notes n ON n.id = l.source_id
            WHERE n.vault_id = @vaultId AND l.resolved_id IS NULL
            """, ("@vaultId", vaultId));
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(new UnresolvedLink(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
        return result;
    }

    public async Task DeleteLinksForNotesAsync(SqliteTransaction tx, IReadOnlyList<long> noteIds)
    {
        if (noteIds.Count == 0) return;
        using var delCmd = context.PrepareById(tx, "DELETE FROM links WHERE source_id = $id", out var pId);
        foreach (var id in noteIds)
        {
            pId.Value = id;
            await delCmd.ExecuteNonQueryAsync();
        }
    }

    public async Task InsertLinksAsync(
        SqliteTransaction tx,
        IReadOnlyList<(long NoteId, string TargetPath)> links)
    {
        if (links.Count == 0) return;
        using var insCmd = context.Connection.CreateCommand();
        insCmd.Transaction = tx;
        insCmd.CommandText = "INSERT INTO links (source_id, target_path) VALUES ($noteId, $target)";
        var pNoteId = insCmd.Parameters.Add("$noteId", SqliteType.Integer);
        var pTarget = insCmd.Parameters.Add("$target", SqliteType.Text);
        foreach (var (noteId, target) in links)
        {
            pNoteId.Value = noteId;
            pTarget.Value = target;
            await insCmd.ExecuteNonQueryAsync();
        }
    }
}
