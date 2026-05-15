using Pumex.Contracts;
using Pumex.Plugin.Sdk;

namespace Pumex.Daemon.Plugins;

internal sealed class InProcessPumexHost : IPumexHost
{
    private readonly IndexDb _db;
    private readonly NoteParser _parser;

    public InProcessPumexHost(IndexDb db, NoteParser parser)
    {
        _db = db;
        _parser = parser;
    }

    public async Task<IReadOnlyList<VaultRecord>> GetVaultsAsync(CancellationToken ct = default)
        => await _db.GetVaultsAsync();

    public async Task<VaultRecord?> ResolveVaultAsync(string nameOrPath, CancellationToken ct = default)
    {
        var byName = await _db.GetVaultByNameAsync(nameOrPath);
        if (byName is not null) return byName;
        return await _db.GetVaultByPathAsync(Path.GetFullPath(nameOrPath));
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string? query,
        long? vaultId = null,
        int limit = 50,
        IReadOnlyList<string>? tags = null,
        IReadOnlyList<KeyValuePair<string, string>>? properties = null,
        CancellationToken ct = default)
        => await _db.SearchAsync(query, limit, vaultId, tags, properties);

    public Task<NoteContent> ReadNoteAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Note not found: {path}");

        var doc = _parser.Parse(path);
        var props = doc.Frontmatter.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "");

        return Task.FromResult(new NoteContent(
            Path: path,
            Raw: doc.RawContent,
            Body: doc.Content,
            Properties: props,
            Tags: doc.Tags,
            OutgoingLinks: doc.OutgoingLinks));
    }

    public async Task<IReadOnlyList<TagCount>> GetTagsAsync(long? vaultId, CancellationToken ct = default)
        => await _db.GetTagsAsync(vaultId);
}
