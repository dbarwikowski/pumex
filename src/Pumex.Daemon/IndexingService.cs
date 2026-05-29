using Pumex.Contracts;

namespace Pumex.Daemon;

public sealed class IndexingService : IDisposable
{
    private readonly VaultRecord _vault;
    private readonly IndexDbContext _context;
    private readonly INoteRepository _noteRepo;
    private readonly ILinkRepository _linkRepo;
    private readonly FormatParserRegistry _parser;
    private readonly WikilinkResolver _resolver;
    private readonly VaultWatcher _watcher;
    private readonly ILogger<IndexingService> _logger;
    private readonly string _configPath;

    // Recomputed from .pumex/config.json on startup and whenever the config file
    // changes. Decides which files are indexed for this vault.
    private VaultIndexPolicy _policy;

    public IndexingService(
        VaultRecord vault,
        IndexDbContext context,
        INoteRepository noteRepo,
        ILinkRepository linkRepo,
        FormatParserRegistry parser,
        WikilinkResolver resolver,
        VaultWatcher watcher,
        ILogger<IndexingService> logger)
    {
        _vault = vault;
        _context = context;
        _noteRepo = noteRepo;
        _linkRepo = linkRepo;
        _parser = parser;
        _resolver = resolver;
        _watcher = watcher;
        _logger = logger;
        _configPath = Path.GetFullPath(PumexPaths.VaultConfigPath(vault.Path));
        _policy = VaultIndexPolicy.Load(vault);
    }

    public VaultRecord Vault => _vault;

    public async Task RunAsync(CancellationToken ct)
    {
        _logger.LogInformation("Vault {Name} ({Path}): starting full scan", _vault.Name, _vault.Path);

        if (!Directory.Exists(_vault.Path))
        {
            _logger.LogWarning("Vault {Name}: path {Path} does not exist; skipping indexing", _vault.Name, _vault.Path);
            return;
        }

        // Start the watcher before the full scan so no events are missed during the scan window.
        try
        {
            _watcher.Start(_vault.Path, ex =>
                _logger.LogWarning(ex, "Vault {Name}: watcher error; stopping watch loop", _vault.Name));
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or DirectoryNotFoundException)
        {
            _logger.LogWarning(ex, "Vault {Name}: cannot start watcher on {Path}", _vault.Name, _vault.Path);
            return;
        }

        await ReindexAllAsync(ct);

        await foreach (var batch in _watcher.ReadBatchesAsync(ct))
        {
            foreach (var evt in batch)
            {
                if (ct.IsCancellationRequested) return;
                await HandleEventAsync(evt, ct);
            }
            await ResolvePendingLinksAsync(ct);
        }
    }

    /// <summary>
    /// Full mtime-diff scan against the current policy, then resolver rebuild and
    /// link resolution. Run on startup and after a config change. Files no longer
    /// matched by the policy (extension disabled, newly ignored) fall out of the
    /// enumeration and are deleted by the leftover-purge below — that is how a
    /// disabled format gets de-indexed.
    /// </summary>
    private async Task ReindexAllAsync(CancellationToken ct)
    {
        await FullScanAsync(ct);

        var allPaths = await _noteRepo.GetAllPathsAsync(_vault.Id);
        _resolver.Rebuild(allPaths);
        await ResolvePendingLinksAsync(ct);

        _logger.LogInformation("Vault {Name}: scan complete, {Count} notes", _vault.Name, allPaths.Count);
    }

    private async Task FullScanAsync(CancellationToken ct)
    {
        var indexed = await _noteRepo.GetAllMtimesAsync(_vault.Id);
        var batch = new List<string>(50);

        IEnumerator<string>? enumerator = null;
        try
        {
            enumerator = _policy.Enumerate(ex =>
                _logger.LogWarning(ex, "Vault {Name}: directory enumeration error; some files may be skipped", _vault.Name))
                .GetEnumerator();
            while (true)
            {
                bool moved;
                try { moved = enumerator.MoveNext(); }
                catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException or IOException)
                {
                    _logger.LogWarning(ex, "Vault {Name}: directory enumeration interrupted; aborting full scan", _vault.Name);
                    return;
                }
                if (!moved) break;

                ct.ThrowIfCancellationRequested();
                var file = enumerator.Current;

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
        }
        finally { enumerator?.Dispose(); }

        if (batch.Count > 0)
            await IndexBatchAsync(batch);

        // Anything still in `indexed` is gone — deleted from disk or no longer
        // matched by the policy (extension disabled / now ignored).
        foreach (var path in indexed.Keys)
        {
            try { await _noteRepo.DeleteNoteAsync(path); }
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
            await UpsertWithTransactionAsync(docs);
    }

    private async Task HandleEventAsync(FileEvent evt, CancellationToken ct)
    {
        try
        {
            // A config change reshapes the active format / ignore set: reload the
            // policy and re-scan. The re-scan indexes newly-enabled files and
            // de-indexes files whose format was just disabled.
            if (string.Equals(Path.GetFullPath(evt.Path), _configPath, StringComparison.OrdinalIgnoreCase))
            {
                _policy = VaultIndexPolicy.Load(_vault);
                _logger.LogInformation("Vault {Name}: config changed, re-scanning", _vault.Name);
                await ReindexAllAsync(ct);
                return;
            }

            switch (evt.Type)
            {
                case FileEventType.Created:
                case FileEventType.Changed:
                    if (!_policy.ShouldIndex(evt.Path) || !File.Exists(evt.Path)) return;
                    var doc = _parser.Parse(evt.Path);
                    await UpsertWithTransactionAsync([doc]);
                    _resolver.Add(evt.Path);
                    break;

                case FileEventType.Deleted:
                    await _noteRepo.DeleteNoteAsync(evt.Path);
                    _resolver.Remove(evt.Path);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process event {Type} {Path}", evt.Type, evt.Path);
        }
    }

    /// <summary>
    /// Performs a full upsert (notes + tags + properties + FTS + links) for the
    /// given documents inside a single transaction. IndexingService owns the
    /// transaction lifecycle as the unit-of-work initiator.
    /// </summary>
    private async Task UpsertWithTransactionAsync(IReadOnlyList<NoteDocument> docs)
    {
        using var gate = await _context.AcquireAsync();
        using var tx = _context.BeginTransaction();
        try
        {
            var result = await _noteRepo.UpsertCoreAsync(tx, _vault.Id, docs);
            await _linkRepo.DeleteLinksForNotesAsync(tx, result.Entries.Select(e => e.Id).ToList());
            await _linkRepo.InsertLinksAsync(tx, result.Links);
            tx.Commit();
            _noteRepo.UpdateCacheUnsafe(result.Entries);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private async Task ResolvePendingLinksAsync(CancellationToken ct)
    {
        foreach (var link in await _linkRepo.GetUnresolvedLinksAsync(_vault.Id))
        {
            ct.ThrowIfCancellationRequested();

            var resolvedPath = _resolver.Resolve(link.TargetText, link.SourcePath);
            if (resolvedPath is null) continue;

            var resolvedId = await _noteRepo.GetNoteIdAsync(resolvedPath);
            if (resolvedId is not null)
                await _linkRepo.SetLinkResolutionAsync(link.SourceId, link.TargetText, resolvedId);
        }
    }

    public void Dispose() => _watcher.Dispose();
}
