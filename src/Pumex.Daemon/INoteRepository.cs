using Microsoft.Data.Sqlite;
using Pumex.Contracts;

namespace Pumex.Daemon;

public interface INoteRepository
{
    Task<Dictionary<string, long>> GetAllMtimesAsync(long vaultId);

    /// <summary>
    /// Bulk-upserts notes (rows, tags, properties, FTS) within a caller-supplied
    /// transaction. The caller must hold the gate (<see cref="IndexDbContext.AcquireAsync"/>)
    /// and commit/rollback the transaction. After commit, call
    /// <see cref="UpdateCacheUnsafe"/> with the returned entries.
    /// </summary>
    Task<UpsertResult> UpsertCoreAsync(SqliteTransaction tx, long vaultId, IEnumerable<NoteDocument> notes);

    Task DeleteNoteAsync(string path);
    Task<List<string>> GetAllPathsAsync(long vaultId);
    Task<List<NoteSummary>> ListNotesAsync(long? vaultId = null, IReadOnlyList<string>? formats = null);
    Task<long?> GetNoteIdAsync(string path);
    Task<string?> GetNotePathByIdAsync(long id);
    Task<List<string>> GetNotePathsByNameAsync(long vaultId, string name);

    /// <summary>
    /// Note paths in the vault whose filename (with extension) matches
    /// <paramref name="fileName"/> case-insensitively. Used to resolve explicit
    /// references like <c>data.csv</c> for read/backlinks.
    /// </summary>
    Task<List<string>> GetNotePathsByFileNameAsync(long vaultId, string fileName);
    Task<List<PropertyEntry>> GetPropertiesAsync(long noteId);
    Task<string?> GetPropertyAsync(long noteId, string key);

    /// <summary>
    /// Updates the in-memory path↔id cache after a committed upsert transaction.
    /// Must be called while holding the gate (gate is not acquired internally).
    /// </summary>
    void UpdateCacheUnsafe(IReadOnlyList<(string Path, long Id)> entries);

    /// <summary>
    /// Removes entries from the in-memory cache while the caller already holds
    /// the gate. Must NOT be called without holding the gate.
    /// </summary>
    void EvictUnsafe(IReadOnlyList<string> paths, IReadOnlyList<long> ids);

    /// <summary>
    /// Removes entries from the in-memory cache after a vault deletion. Acquires
    /// its own gate internally — do not call while holding the gate.
    /// </summary>
    Task EvictAsync(IReadOnlyList<string> paths, IReadOnlyList<long> ids);
}

/// <summary>
/// Produced by <see cref="INoteRepository.UpsertCoreAsync"/>; carries the data
/// needed to update the link table and the in-memory cache after commit.
/// </summary>
public record UpsertResult(
    IReadOnlyList<(string Path, long Id)> Entries,
    IReadOnlyList<(long NoteId, string TargetPath)> Links
);
