using Pumex.Contracts;

namespace Pumex.Daemon;

public sealed class IndexingService : IDisposable
{
    private readonly VaultRecord _vault;
    private readonly IndexDb _db;
    private readonly NoteParser _parser;
    private readonly WikilinkResolver _resolver;
    private readonly VaultWatcher _watcher;
    private readonly ILogger<IndexingService> _logger;

    public IndexingService(
        VaultRecord vault,
        IndexDb db,
        NoteParser parser,
        WikilinkResolver resolver,
        VaultWatcher watcher,
        ILogger<IndexingService> logger)
    {
        _vault = vault;
        _db = db;
        _parser = parser;
        _resolver = resolver;
        _watcher = watcher;
        _logger = logger;
    }

    public VaultRecord Vault => _vault;

    public async Task RunAsync(CancellationToken ct)
    {
        _logger.LogInformation("Vault {Name} ({Path}): starting full scan", _vault.Name, _vault.Path);

        await FullScanAsync(ct);

        var allPaths = await _db.GetAllPathsAsync(_vault.Id);
        _resolver.Rebuild(allPaths);
        await ResolvePendingLinksAsync(ct);

        _logger.LogInformation("Vault {Name}: scan complete, {Count} notes", _vault.Name, allPaths.Count);

        _watcher.Start(_vault.Path);

        await foreach (var batch in _watcher.ReadBatchesAsync(ct))
        {
            foreach (var evt in batch)
            {
                if (ct.IsCancellationRequested) return;
                await HandleEventAsync(evt);
            }
            await ResolvePendingLinksAsync(ct);
        }
    }

    private async Task FullScanAsync(CancellationToken ct)
    {
        var indexed = await _db.GetAllMtimesAsync(_vault.Id);
        var batch = new List<string>(50);

        foreach (var file in Directory.EnumerateFiles(_vault.Path, "*.md", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();

            var mtime = new DateTimeOffset(File.GetLastWriteTimeUtc(file)).ToUnixTimeSeconds();
            if (indexed.TryGetValue(file, out var indexedMtime))
            {
                indexed.Remove(file);
                if (mtime == indexedMtime) continue;
            }

            batch.Add(file);
            if (batch.Count >= 50)
            {
                await IndexBatchAsync(batch);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
            await IndexBatchAsync(batch);

        // Anything still in `indexed` is gone from disk.
        foreach (var path in indexed.Keys)
        {
            try { await _db.DeleteNoteAsync(path); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to remove deleted note {Path}", path); }
        }
    }

    private async Task IndexBatchAsync(IReadOnlyList<string> files)
    {
        var docs = new List<NoteDocument>(files.Count);
        foreach (var file in files)
        {
            try { docs.Add(_parser.Parse(file)); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to parse {File}", file); }
        }

        if (docs.Count > 0)
            await _db.UpsertNotesAsync(_vault.Id, docs);
    }

    private async Task HandleEventAsync(FileEvent evt)
    {
        try
        {
            switch (evt.Type)
            {
                case FileEventType.Created:
                case FileEventType.Changed:
                    if (!File.Exists(evt.Path)) return;
                    var doc = _parser.Parse(evt.Path);
                    await _db.UpsertNotesAsync(_vault.Id, [doc]);
                    _resolver.Add(evt.Path);
                    break;

                case FileEventType.Deleted:
                    await _db.DeleteNoteAsync(evt.Path);
                    _resolver.Remove(evt.Path);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process event {Type} {Path}", evt.Type, evt.Path);
        }
    }

    private async Task ResolvePendingLinksAsync(CancellationToken ct)
    {
        foreach (var link in await _db.GetUnresolvedLinksAsync(_vault.Id))
        {
            ct.ThrowIfCancellationRequested();

            var resolvedPath = _resolver.Resolve(link.TargetText, link.SourcePath);
            if (resolvedPath is null) continue;

            var resolvedId = await _db.GetNoteIdAsync(resolvedPath);
            if (resolvedId is not null)
                await _db.SetLinkResolutionAsync(link.SourceId, link.TargetText, resolvedId);
        }
    }

    public void Dispose() => _watcher.Dispose();
}
