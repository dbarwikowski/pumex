using Microsoft.Data.Sqlite;
using Pumex.Contracts;

namespace Pumex.Daemon;

public interface ILinkRepository
{
    Task<List<string>> GetBacklinksAsync(string path, long? vaultId = null);
    Task SetLinkResolutionAsync(long sourceId, string targetText, long? resolvedId);
    Task<List<UnresolvedLink>> GetUnresolvedLinksAsync(long vaultId);

    /// <summary>
    /// Deletes all outgoing link rows for the given note IDs within a
    /// caller-supplied transaction. Call while holding the gate.
    /// </summary>
    Task DeleteLinksForNotesAsync(SqliteTransaction tx, IReadOnlyList<long> noteIds);

    /// <summary>
    /// Inserts outgoing link rows within a caller-supplied transaction.
    /// Call while holding the gate.
    /// </summary>
    Task InsertLinksAsync(SqliteTransaction tx, IReadOnlyList<(long NoteId, string TargetPath)> links);
}
