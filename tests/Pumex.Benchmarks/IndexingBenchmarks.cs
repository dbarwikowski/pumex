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
        using var db = new IndexDb(dbPath);
        await db.AddVaultAsync("bench", _vaultRoot);
        var vault = (await db.GetVaultByPathAsync(_vaultRoot))!;

        await IndexingBench.RunInitialScanAsync(db, vault);
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

        using var db = new IndexDb(_dbPath);
        await db.AddVaultAsync("bench", _vaultRoot);
        var vault = (await db.GetVaultByPathAsync(_vaultRoot))!;
        await IndexingBench.RunInitialScanAsync(db, vault);
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
        using var db = new IndexDb(_dbPath);
        var vault = (await db.GetVaultByPathAsync(_vaultRoot))!;
        await IndexingBench.RunInitialScanAsync(db, vault);
    }
}

internal static class IndexingBench
{
    public static async Task RunInitialScanAsync(IndexDb db, VaultRecord vault)
    {
        // Inlined replica of IndexingService.FullScanAsync to isolate the cost
        // of disk walk + parse + upsert without the watcher / link-resolution
        // tail. Keeping it here means the benchmark is stable across refactors
        // of the production service's surrounding code.
        var parser = new NoteParser();
        var indexed = await db.GetAllMtimesAsync(vault.Id);
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
                await db.UpsertNotesAsync(vault.Id, batch);
                batch.Clear();
            }
        }
        if (batch.Count > 0) await db.UpsertNotesAsync(vault.Id, batch);

        foreach (var path in indexed.Keys)
            await db.DeleteNoteAsync(path);
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

        using (var db = new IndexDb(_dbPath))
        {
            _vaultId = await db.AddVaultAsync("bench", _vaultRoot);
            var vault = (await db.GetVaultByPathAsync(_vaultRoot))!;
            await PrimeAsync(db, vault);
        }

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
        using var db = new IndexDb(_dbPath);
        var doc = _parser.Parse(_targetPath);
        await db.UpsertNotesAsync(_vaultId, [doc]);
    }

    private async Task PrimeAsync(IndexDb db, VaultRecord vault)
    {
        var batch = new List<NoteDocument>(50);
        foreach (var file in Directory.EnumerateFiles(vault.Path, "*.md", SearchOption.AllDirectories))
        {
            batch.Add(_parser.Parse(file));
            if (batch.Count >= 50)
            {
                await db.UpsertNotesAsync(vault.Id, batch);
                batch.Clear();
            }
        }
        if (batch.Count > 0) await db.UpsertNotesAsync(vault.Id, batch);
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
    private IndexDb _db = null!;
    private long _vaultId;

    [GlobalSetup]
    public async Task Setup()
    {
        _vaultRoot = BenchmarkVaultBuilder.Build(VaultSize);
        _dbDir = Path.Combine(Path.GetTempPath(), "pumex-bench-db-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dbDir);
        _db = new IndexDb(Path.Combine(_dbDir, "search.db"));
        _vaultId = await _db.AddVaultAsync("bench", _vaultRoot);
        var vault = (await _db.GetVaultByPathAsync(_vaultRoot))!;

        var parser = new NoteParser();
        var batch = new List<NoteDocument>(50);
        foreach (var file in Directory.EnumerateFiles(vault.Path, "*.md", SearchOption.AllDirectories))
        {
            batch.Add(parser.Parse(file));
            if (batch.Count >= 50)
            {
                await _db.UpsertNotesAsync(vault.Id, batch);
                batch.Clear();
            }
        }
        if (batch.Count > 0) await _db.UpsertNotesAsync(vault.Id, batch);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _db.Dispose();
        BenchmarkVaultBuilder.Cleanup(_vaultRoot);
        try { Directory.Delete(_dbDir, recursive: true); } catch { }
    }

    [Benchmark]
    public async Task<int> Search_common_term_global()
    {
        var hits = await _db.SearchAsync("context");
        return hits.Count;
    }

    [Benchmark]
    public async Task<int> Search_common_term_scoped()
    {
        var hits = await _db.SearchAsync("context", vaultId: _vaultId);
        return hits.Count;
    }
}

