using Pumex.Contracts;

namespace Pumex.Daemon.Ipc;

public class VaultsHandler : ICommandHandler
{
    private readonly IVaultRepository _vaults;

    public string Command => "vaults";

    public VaultsHandler(IVaultRepository vaults) => _vaults = vaults;

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct) =>
        await _vaults.GetVaultsAsync();
}

public class VaultAddHandler : ICommandHandler
{
    private readonly IVaultRepository _vaults;
    private readonly VaultIndexingOrchestrator _orchestrator;

    public string Command => "vault:add";

    public VaultAddHandler(IVaultRepository vaults, VaultIndexingOrchestrator orchestrator)
    {
        _vaults = vaults;
        _orchestrator = orchestrator;
    }

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        if (!request.Args.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("name is required");
        if (!request.Args.TryGetValue("path", out var path) || string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("path is required");

        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
            throw new ArgumentException($"Path does not exist: {fullPath}");

        var existing = await _vaults.GetVaultByPathAsync(fullPath);
        if (existing is not null && existing.Name != name)
            throw new ArgumentException($"path already registered as vault '{existing.Name}'; remove it first.");

        await _vaults.AddVaultAsync(name, fullPath);
        var vault = await _vaults.GetVaultByPathAsync(fullPath)
            ?? throw new InvalidOperationException("Vault row missing after insert");

        await _orchestrator.AddVaultAsync(vault);
        return vault;
    }
}

public class VaultRemoveHandler : ICommandHandler
{
    private readonly IVaultRepository _vaults;
    private readonly VaultIndexingOrchestrator _orchestrator;

    public string Command => "vault:remove";

    public VaultRemoveHandler(IVaultRepository vaults, VaultIndexingOrchestrator orchestrator)
    {
        _vaults = vaults;
        _orchestrator = orchestrator;
    }

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var name = request.Require("name");
        var vault = await _vaults.GetVaultByNameAsync(name)
            ?? throw new ArgumentException($"vault not found: {name}");

        // Stop the indexer first so the watcher releases its file handles
        // before we drop the DB rows.
        await _orchestrator.RemoveVaultAsync(vault.Id);
        await _vaults.RemoveVaultAsync(vault.Id);

        // We deliberately do not delete the on-disk .pumex/ marker — the user's
        // notes stay intact. Re-registering with `vault add` reuses the same
        // marker without surprises.
        return vault;
    }
}
