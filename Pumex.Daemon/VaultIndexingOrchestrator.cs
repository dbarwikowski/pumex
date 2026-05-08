using Pumex.Contracts;

namespace Pumex.Daemon;

public class VaultIndexingOrchestrator : BackgroundService
{
    private readonly IndexDb _db;
    private readonly IndexingServiceFactory _factory;
    private readonly ILogger<VaultIndexingOrchestrator> _logger;

    private readonly object _lock = new();
    private readonly Dictionary<long, VaultRunner> _runners = new();
    private CancellationTokenSource? _hostCts;

    private sealed record VaultRunner(IndexingService Service, Task Task, CancellationTokenSource Cts);

    public VaultIndexingOrchestrator(
        IndexDb db,
        IndexingServiceFactory factory,
        ILogger<VaultIndexingOrchestrator> logger)
    {
        _db = db;
        _factory = factory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _hostCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        foreach (var vault in await _db.GetVaultsAsync())
            StartVault(vault);

        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { }

        Task[] outstanding;
        lock (_lock) outstanding = _runners.Values.Select(r => r.Task).ToArray();
        await Task.WhenAll(outstanding.Select(t => t.ContinueWith(_ => { })));
    }

    public Task AddVaultAsync(VaultRecord vault)
    {
        StartVault(vault);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops indexing for a single vault without affecting the others.
    /// Cancels the per-vault token, awaits the runner task (which disposes
    /// the service in its finally), then drops it from the registry.
    /// </summary>
    public async Task RemoveVaultAsync(long vaultId)
    {
        VaultRunner? runner;
        lock (_lock)
        {
            if (!_runners.TryGetValue(vaultId, out runner)) return;
            _runners.Remove(vaultId);
        }

        runner.Cts.Cancel();
        try { await runner.Task; }
        catch { /* RunAsync's catch already logged anything interesting */ }
        runner.Cts.Dispose();
    }

    private void StartVault(VaultRecord vault)
    {
        var hostCts = _hostCts
            ?? throw new InvalidOperationException("Orchestrator hasn't started yet.");

        lock (_lock)
        {
            if (_runners.ContainsKey(vault.Id)) return;

            var cts = CancellationTokenSource.CreateLinkedTokenSource(hostCts.Token);
            var svc = _factory.Create(vault);
            var task = Task.Run(async () =>
            {
                try { await svc.RunAsync(cts.Token); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { _logger.LogError(ex, "Vault {Name} crashed", vault.Name); }
                finally { svc.Dispose(); }
            }, cts.Token);

            _runners[vault.Id] = new VaultRunner(svc, task, cts);
        }
    }
}
