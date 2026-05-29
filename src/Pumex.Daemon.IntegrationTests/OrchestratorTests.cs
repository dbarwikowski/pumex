using Microsoft.Extensions.Logging.Abstractions;
using Pumex.Contracts;
using Pumex.Daemon.IntegrationTests.Helpers;

namespace Pumex.Daemon.IntegrationTests;

public class OrchestratorTests : IDisposable
{
    private readonly string _sandbox;
    private readonly IndexDbContext _context;
    private readonly IVaultRepository _vaults;
    private readonly INoteRepository _notes;
    private readonly ILinkRepository _links;
    private readonly ISearchRepository _search;

    public OrchestratorTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "pumex-int-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        _context = new IndexDbContext(Path.Combine(_sandbox, "index.db"));
        new IndexSchema(_context).Apply();
        _notes = new NoteRepository(_context);
        _links = new LinkRepository(_context);
        _vaults = new VaultRepository(_context, _notes);
        _search = new SearchRepository(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        try { Directory.Delete(_sandbox, recursive: true); }
        catch { /* test cleanup is best-effort */ }
    }

    private VaultIndexingOrchestrator BuildOrchestrator() =>
        new(_vaults,
            new IndexingServiceFactory(
                _context, _notes, _links, FormatParserRegistry.Default(),
                NullLogger<IndexingService>.Instance),
            NullLogger<VaultIndexingOrchestrator>.Instance);

    private string CreateVaultDir(string name)
    {
        var dir = Path.Combine(_sandbox, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task Vaults_already_in_db_are_picked_up_at_startup()
    {
        var alphaDir = CreateVaultDir("alpha");
        File.WriteAllText(Path.Combine(alphaDir, "n.md"), "body with #hello\n");
        await _vaults.AddVaultAsync("alpha", alphaDir);
        var alpha = (await _vaults.GetVaultByPathAsync(alphaDir))!;

        var orchestrator = BuildOrchestrator();
        using var cts = new CancellationTokenSource();
        var run = orchestrator.StartAsync(cts.Token);
        await run; // ExecuteAsync only awaits Task.Delay(infinite), StartAsync returns once the loop is running.

        try
        {
            await AsyncPolling.UntilAsync(
                async () => (await _notes.GetAllPathsAsync(alpha.Id)).Count == 1,
                timeoutMs: 8000,
                message: "vault alpha was not auto-indexed");
        }
        finally
        {
            await orchestrator.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RemoveVaultAsync_stops_a_single_vault_without_taking_down_the_others()
    {
        // Set up two vaults, both with one note.
        var alphaDir = CreateVaultDir("alpha");
        File.WriteAllText(Path.Combine(alphaDir, "a.md"), "alpha body\n");
        var betaDir = CreateVaultDir("beta");
        File.WriteAllText(Path.Combine(betaDir, "b.md"), "beta body\n");
        await _vaults.AddVaultAsync("alpha", alphaDir);
        await _vaults.AddVaultAsync("beta",  betaDir);
        var alpha = (await _vaults.GetVaultByPathAsync(alphaDir))!;
        var beta  = (await _vaults.GetVaultByPathAsync(betaDir))!;

        var orchestrator = BuildOrchestrator();
        using var cts = new CancellationTokenSource();
        await orchestrator.StartAsync(cts.Token);

        try
        {
            await AsyncPolling.UntilAsync(
                async () => (await _notes.GetAllPathsAsync(alpha.Id)).Count == 1
                         && (await _notes.GetAllPathsAsync(beta.Id)).Count == 1,
                timeoutMs: 8000);

            await orchestrator.RemoveVaultAsync(alpha.Id);

            // Beta keeps indexing — write a new file and verify it gets picked up.
            File.WriteAllText(Path.Combine(betaDir, "b2.md"), "still indexing, marker word: pangolin\n");
            await AsyncPolling.UntilAsync(
                async () => (await _search.SearchAsync("pangolin", vaultId: beta.Id)).Count == 1,
                timeoutMs: 8000,
                message: "beta vault stopped indexing after alpha was removed");
        }
        finally
        {
            await orchestrator.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task AddVaultAsync_starts_indexing_a_new_vault_at_runtime()
    {
        // Orchestrator boots with no vaults registered.
        var orchestrator = BuildOrchestrator();
        using var cts = new CancellationTokenSource();
        await orchestrator.StartAsync(cts.Token);

        try
        {
            var betaDir = CreateVaultDir("beta");
            File.WriteAllText(Path.Combine(betaDir, "live.md"), "added live, marker word: pangolin\n");
            await _vaults.AddVaultAsync("beta", betaDir);
            var beta = (await _vaults.GetVaultByPathAsync(betaDir))!;

            await orchestrator.AddVaultAsync(beta);

            await AsyncPolling.UntilAsync(
                async () =>
                {
                    var hits = await _search.SearchAsync("pangolin", vaultId: beta.Id);
                    return hits.Count == 1;
                },
                timeoutMs: 8000);
        }
        finally
        {
            await orchestrator.StopAsync(CancellationToken.None);
        }
    }
}
