using Pumex.Contracts;

namespace Pumex.Daemon;

public class VaultIndexingOrchestrator : BackgroundService
{
    private readonly IndexDb _db;
    private readonly IndexingServiceFactory _factory;
    private readonly ILogger<VaultIndexingOrchestrator> _logger;

    private readonly object _lock = new();
    private readonly Dictionary<long, VaultRunner> _runners = new();
    // Standalone CTS: cancelled explicitly in ExecuteAsync cleanup so vault runners stop on host shutdown.
    // Initialized in field so AddVaultAsync is safe to call as soon as StartAsync returns.
    private readonly CancellationTokenSource _hostCts = new();

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
        foreach (var vault in await _db.GetVaultsAsync())
            StartVault(vault);

        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { }

        // Signal all vault runners to stop.
        _hostCts.Cancel();

        Task[] outstanding;
        lock (_lock) outstanding = _runners.Values.Select(r => r.Task).ToArray();
        await Task.WhenAll(outstanding.Select(t => t.ContinueWith(_ => { })));
    }

    public Task AddVaultAsync(VaultRecord vault)
    {
        StartVault(vault);
        return Task.CompletedTask;
    }

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

    public override void Dispose()
    {
        _hostCts.Cancel();
        _hostCts.Dispose();
        base.Dispose();
    }

    private void StartVault(VaultRecord vault)
    {
        lock (_lock)
        {
            if (_runners.ContainsKey(vault.Id)) return;

            var cts = CancellationTokenSource.CreateLinkedTokenSource(_hostCts.Token);
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
