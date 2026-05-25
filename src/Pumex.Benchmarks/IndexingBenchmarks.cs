using BenchmarkDotNet.Attributes;
using Pumex.Contracts;
using Pumex.Daemon;

namespace Pumex.Benchmarks;

/// <summary>
/// Cold full scan: empty index, every note parsed and inserted from scratch.
/// </summary>
[MemoryDiagnoser]
public class ColdFullScanBenchmarks
{
    [Params(100, 1000, 10_000)]
    public int VaultSize;

    private string _vaultRoot = null!;
    private string _dbDir = null!;

    [GlobalSetup]
    public void Setup()
    {
        _vaultRoot = BenchmarkVaultBuilder.Build(VaultSize);
        _dbDir = Path.Combine(Path.GetTempPath(), "pumex-bench-db-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dbDir);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        BenchmarkVaultBuilder.Cleanup(_vaultRoot);
        try { Directory.Delete(_dbDir, recursive: true); } catch { }
    }

    [Benchmark]
    public async Task ColdFullScan()
    {
        var dbPath = Path.Combine(_dbDir, $"cold-{Guid.NewGuid():N}.db");
        var ctx = new IndexDbContext(dbPath);
        new IndexSchema(ctx).Apply();
        var notes = new NoteRepository(ctx);
        var links = new LinkRepository(ctx);
        var vaults = new VaultRepository(ctx, notes);

        await vaults.AddVaultAsync("bench", _vaultRoot);
        var vault = (await vaults.GetVaultByPathAsync(_vaultRoot))!;

        await IndexingBench.RunInitialScanAsync(ctx, notes, links, vault);
        ctx.Dispose();
    }
}

/// <summary>
/// Warm full scan: index already populated, no files have changed. Hits the
/// mtime-match short-circuit. Prime happens in <see cref="Setup"/> so the
/// measured body is only the rescan pass.
/// </summary>
[MemoryDiagnoser]
public class WarmFullScanBenchmarks
{
    [Params(100, 1000, 10_000)]
    public int VaultSize;

    private string _vaultRoot = null!;
    private string _dbDir = null!;
    private string _dbPath = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _vaultRoot = BenchmarkVaultBuilder.Build(VaultSize);
        _dbDir = Path.Combine(Path.GetTempPath(), "pumex-bench-db-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dbDir);
        _dbPath = Path.Combine(_dbDir, "warm.db");

        var ctx = new IndexDbContext(_dbPath);
        new IndexSchema(ctx).Apply();
        var notes = new NoteRepository(ctx);
        var links = new LinkRepository(ctx);
        var vaults = new VaultRepository(ctx, notes);

        await vaults.AddVaultAsync("bench", _vaultRoot);
        var vault = (await vaults.GetVaultByPathAsync(_vaultRoot))!;
        await IndexingBench.RunInitialScanAsync(ctx, notes, links, vault);
        ctx.Dispose();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        BenchmarkVaultBuilder.Cleanup(_vaultRoot);
        try { Directory.Delete(_dbDir, recursive: true); } catch { }
    }

    [Benchmark]
    public async Task WarmFullScan()
    {
        var ctx = new IndexDbContext(_dbPath);
        new IndexSchema(ctx).Apply();
        var notes = new NoteRepository(ctx);
        var links = new LinkRepository(ctx);
        var vaults = new VaultRepository(ctx, notes);

        var vault = (await vaults.GetVaultByPathAsync(_vaultRoot))!;
        await IndexingBench.RunInitialScanAsync(ctx, notes, links, vault);
        ctx.Dispose();
    }
}

internal static class IndexingBench
{
    public static async Task RunInitialScanAsync(
        IndexDbContext context,
        INoteRepository noteRepo,
        ILinkRepository linkRepo,
        VaultRecord vault)
    {
        // Inlined replica of IndexingService.FullScanAsync to isolate the cost
        // of disk walk + parse + upsert without the watcher / link-resolution
        // tail. Keeping it here means the benchmark is stable across refactors
        // of the production service's surrounding code.
        var parser = new NoteParser();
        var indexed = await noteRepo.GetAllMtimesAsync(vault.Id);
        var batch = new List<NoteDocument>(50);

        foreach (var file in Directory.EnumerateFiles(vault.Path, "*.md", SearchOption.AllDirectories))
        {
            var mtime = new DateTimeOffset(File.GetLastWriteTimeUtc(file)).ToUnixTimeSeconds();
            if (indexed.TryGetValue(file, out var indexedMtime))
            {
                indexed.Remove(file);
                if (mtime == indexedMtime) continue;
            }

            batch.Add(parser.Parse(file));
            if (batch.Count >= 50)
            {
                await UpsertBatchAsync(context, noteRepo, linkRepo, vault.Id, batch);
                batch.Clear();
            }
        }
        if (batch.Count > 0)
            await UpsertBatchAsync(context, noteRepo, linkRepo, vault.Id, batch);

        foreach (var path in indexed.Keys)
            await noteRepo.DeleteNoteAsync(path);
    }

    internal static async Task UpsertBatchAsync(
        IndexDbContext context,
        INoteRepository noteRepo,
        ILinkRepository linkRepo,
        long vaultId,
        IReadOnlyList<NoteDocument> docs)
    {
        using var gate = await context.AcquireAsync();
        using var tx = context.BeginTransaction();
        try
        {
            var result = await noteRepo.UpsertCoreAsync(tx, vaultId, docs);
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
}

/// <summary>
/// Per-event cost on a populated index: the hot path from a <see cref="VaultWatcher"/>
/// debounced batch. Measures parse + upsert for one note.
/// </summary>
[MemoryDiagnoser]
public class IncrementalUpdateBenchmarks
{
    [Params(100, 1000, 10_000)]
    public int VaultSize;

    private string _vaultRoot = null!;
    private string _dbDir = null!;
    private string _dbPath = null!;
    private NoteParser _parser = null!;
    private string _targetPath = null!;
    private long _vaultId;

    [GlobalSetup]
    public async Task Setup()
    {
        _vaultRoot = BenchmarkVaultBuilder.Build(VaultSize);
        _dbDir = Path.Combine(Path.GetTempPath(), "pumex-bench-db-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dbDir);
        _dbPath = Path.Combine(_dbDir, "incremental.db");
        _parser = new NoteParser();

        var ctx = new IndexDbContext(_dbPath);
        new IndexSchema(ctx).Apply();
        var notes = new NoteRepository(ctx);
        var links = new LinkRepository(ctx);
        var vaults = new VaultRepository(ctx, notes);

        _vaultId = await vaults.AddVaultAsync("bench", _vaultRoot);
        var vault = (await vaults.GetVaultByPathAsync(_vaultRoot))!;
        await PrimeAsync(ctx, notes, links, vault);
        ctx.Dispose();

        _targetPath = Directory.EnumerateFiles(_vaultRoot, "*.md").Skip(VaultSize / 2).First();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        BenchmarkVaultBuilder.Cleanup(_vaultRoot);
        try { Directory.Delete(_dbDir, recursive: true); } catch { }
    }

    [Benchmark]
    public async Task ParseAndUpsert_one_note()
    {
        // Reopen the DB inside the benchmark so we measure realistic latency
        // including a fresh connection. SQLite's WAL mode keeps this cheap.
        var ctx = new IndexDbContext(_dbPath);
        new IndexSchema(ctx).Apply();
        var notes = new NoteRepository(ctx);
        var links = new LinkRepository(ctx);

        var doc = _parser.Parse(_targetPath);
        await IndexingBench.UpsertBatchAsync(ctx, notes, links, _vaultId, [doc]);
        ctx.Dispose();
    }

    private static async Task PrimeAsync(
        IndexDbContext context, INoteRepository noteRepo, ILinkRepository linkRepo, VaultRecord vault)
    {
        var parser = new NoteParser();
        var batch = new List<NoteDocument>(50);
        foreach (var file in Directory.EnumerateFiles(vault.Path, "*.md", SearchOption.AllDirectories))
        {
            batch.Add(parser.Parse(file));
            if (batch.Count >= 50)
            {
                await IndexingBench.UpsertBatchAsync(context, noteRepo, linkRepo, vault.Id, batch);
                batch.Clear();
            }
        }
        if (batch.Count > 0)
            await IndexingBench.UpsertBatchAsync(context, noteRepo, linkRepo, vault.Id, batch);
    }
}

/// <summary>FTS5 search latency over a populated index.</summary>
[MemoryDiagnoser]
public class SearchBenchmarks
{
    [Params(100, 1000, 10_000)]
    public int VaultSize;

    private string _vaultRoot = null!;
    private string _dbDir = null!;
    private IndexDbContext _context = null!;
    private ISearchRepository _search = null!;
    private long _vaultId;

    [GlobalSetup]
    public async Task Setup()
    {
        _vaultRoot = BenchmarkVaultBuilder.Build(VaultSize);
        _dbDir = Path.Combine(Path.GetTempPath(), "pumex-bench-db-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dbDir);
        _context = new IndexDbContext(Path.Combine(_dbDir, "search.db"));
        new IndexSchema(_context).Apply();
        var notes = new NoteRepository(_context);
        var links = new LinkRepository(_context);
        var vaults = new VaultRepository(_context, notes);
        _search = new SearchRepository(_context);

        _vaultId = await vaults.AddVaultAsync("bench", _vaultRoot);
        var vault = (await vaults.GetVaultByPathAsync(_vaultRoot))!;

        await PrimeAsync(_context, notes, links, vault);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _context.Dispose();
        BenchmarkVaultBuilder.Cleanup(_vaultRoot);
        try { Directory.Delete(_dbDir, recursive: true); } catch { }
    }

    [Benchmark]
    public async Task<int> Search_common_term_global()
    {
        var hits = await _search.SearchAsync("context");
        return hits.Count;
    }

    [Benchmark]
    public async Task<int> Search_common_term_scoped()
    {
        var hits = await _search.SearchAsync("context", vaultId: _vaultId);
        return hits.Count;
    }

    private static async Task PrimeAsync(
        IndexDbContext context, INoteRepository noteRepo, ILinkRepository linkRepo, VaultRecord vault)
    {
        var parser = new NoteParser();
        var batch = new List<NoteDocument>(50);
        foreach (var file in Directory.EnumerateFiles(vault.Path, "*.md", SearchOption.AllDirectories))
        {
            batch.Add(parser.Parse(file));
            if (batch.Count >= 50)
            {
                await IndexingBench.UpsertBatchAsync(context, noteRepo, linkRepo, vault.Id, batch);
                batch.Clear();
            }
        }
        if (batch.Count > 0)
            await IndexingBench.UpsertBatchAsync(context, noteRepo, linkRepo, vault.Id, batch);
    }
}
