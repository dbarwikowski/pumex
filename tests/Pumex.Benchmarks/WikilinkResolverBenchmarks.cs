using BenchmarkDotNet.Attributes;
using Pumex.Daemon;

namespace Pumex.Benchmarks;

/// <summary>
/// CLAUDE.md flags <c>WikilinkResolver.Resolve</c> as an O(n) hotspot at scale.
/// Track Resolve under representative vault sizes so a future suffix-index
/// optimisation has a baseline to compare against.
/// </summary>
[MemoryDiagnoser]
public class WikilinkResolverBenchmarks
{
    [Params(100, 1000, 10_000)]
    public int VaultSize;

    private WikilinkResolver _resolver = null!;
    private string[] _allPaths = null!;
    private string _hitName = null!;
    private string _missName = null!;

    [GlobalSetup]
    public void Setup()
    {
        _allPaths = new string[VaultSize];
        for (var i = 0; i < VaultSize; i++)
            _allPaths[i] = System.IO.Path.Combine("vault", $"folder-{i % 20}", $"note-{i:D6}.md");

        _resolver = new WikilinkResolver();
        _resolver.Rebuild(_allPaths);
        _hitName = $"note-{VaultSize / 2:D6}";
        _missName = "note-does-not-exist";
    }

    [Benchmark]
    public string? Resolve_hit_by_name()
        => _resolver.Resolve(_hitName, sourcePath: _allPaths[0]);

    [Benchmark]
    public string? Resolve_miss()
        => _resolver.Resolve(_missName, sourcePath: _allPaths[0]);

    [Benchmark]
    public void Rebuild()
        => _resolver.Rebuild(_allPaths);
}
