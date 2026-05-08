using Pumex.Contracts;

namespace Pumex.Daemon.Ipc;

public class BacklinksHandler : ICommandHandler
{
    private readonly IndexDb _db;

    public string Command => "backlinks";

    public BacklinksHandler(IndexDb db) => _db = db;

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var vault = await request.ResolveVaultAsync(_db);
        var path = IpcRequestExtensions.ResolveNotePath(request.Require("path"), vault);
        return await _db.GetBacklinksAsync(path, vault?.Id);
    }
}
