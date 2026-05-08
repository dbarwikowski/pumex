using Pumex.Contracts;

namespace Pumex.Daemon.Ipc;

public class SearchHandler : ICommandHandler
{
    private readonly IndexDb _db;

    public string Command => "search";

    public SearchHandler(IndexDb db) => _db = db;

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var query = request.Require("query");
        var limit = request.Args.TryGetValue("limit", out var l) && int.TryParse(l, out var n) ? n : 50;
        var vault = await request.ResolveVaultAsync(_db);
        return await _db.SearchAsync(query, limit, vault?.Id);
    }
}
