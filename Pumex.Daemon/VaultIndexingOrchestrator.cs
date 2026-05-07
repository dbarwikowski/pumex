using Pumex.Contracts;

namespace Pumex.Daemon;

public class VaultIndexingOrchestrator : BackgroundService
{
    private readonly IndexDb _db;
    private readonly IndexingServiceFactory _factory;
    private readonly ILogger<VaultIndexingOrchestrator> _logger;

    private readonly object _lock = new();
    private readonly Dictionary<long, IndexingService> _services = new();
    private readonly Dictionary<long, Task> _tasks = new();
    private CancellationToken _runToken;

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
        _runToken = ct;

        foreach (var vault in await _db.GetVaultsAsync())
            StartVault(vault);

        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { }

        Task[] outstanding;
        lock (_lock) outstanding = _tasks.Values.ToArray();
        await Task.WhenAll(outstanding.Select(t => t.ContinueWith(_ => { })));
    }

    public Task AddVaultAsync(VaultRecord vault)
    {
        StartVault(vault);
        return Task.CompletedTask;
    }

    private void StartVault(VaultRecord vault)
    {
        IndexingService svc;
        lock (_lock)
        {
            if (_services.ContainsKey(vault.Id)) return;
            svc = _factory.Create(vault);
            _services[vault.Id] = svc;
        }

        var task = Task.Run(async () =>
        {
            try { await svc.RunAsync(_runToken); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _logger.LogError(ex, "Vault {Name} crashed", vault.Name); }
            finally { svc.Dispose(); }
        }, _runToken);

        lock (_lock) _tasks[vault.Id] = task;
    }
}
