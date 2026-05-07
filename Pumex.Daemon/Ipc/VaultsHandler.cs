using Pumex.Contracts;

namespace Pumex.Daemon.Ipc;

public class VaultsHandler : ICommandHandler
{
    private readonly IndexDb _db;

    public string Command => "vaults";

    public VaultsHandler(IndexDb db) => _db = db;

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct) =>
        await _db.GetVaultsAsync();
}

public class VaultAddHandler : ICommandHandler
{
    private readonly IndexDb _db;
    private readonly VaultIndexingOrchestrator _orchestrator;

    public string Command => "vault:add";

    public VaultAddHandler(IndexDb db, VaultIndexingOrchestrator orchestrator)
    {
        _db = db;
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

        await _db.AddVaultAsync(name, fullPath);
        var vault = await _db.GetVaultByPathAsync(fullPath)
            ?? throw new InvalidOperationException("Vault row missing after insert");

        await _orchestrator.AddVaultAsync(vault);
        return vault;
    }
}
