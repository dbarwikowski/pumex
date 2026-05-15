using Pumex.Contracts;

namespace Pumex.Plugin.Sdk;

// Capability surface. Same interface for in-proc and out-of-proc plugins —
// in-proc impl wraps IndexDb directly; out-of-proc impl (lands in 001B) wraps
// an IpcClient pointing back at the daemon. Keep this small; grow it
// deliberately. Reads only for v1 — write-capable plugins need an audit story
// we don't have yet.
public interface IPumexHost
{
    Task<IReadOnlyList<VaultRecord>> GetVaultsAsync(CancellationToken ct = default);

    Task<VaultRecord?> ResolveVaultAsync(string nameOrPath, CancellationToken ct = default);

    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string? query,
        long? vaultId = null,
        int limit = 50,
        IReadOnlyList<string>? tags = null,
        IReadOnlyList<KeyValuePair<string, string>>? properties = null,
        CancellationToken ct = default);

    Task<NoteContent> ReadNoteAsync(string path, CancellationToken ct = default);

    Task<IReadOnlyList<TagCount>> GetTagsAsync(long? vaultId, CancellationToken ct = default);
}
