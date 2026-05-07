using Pumex.Contracts;

namespace Pumex.Daemon.Ipc;

public class SearchHandler : ICommandHandler
{
    private readonly IndexDb _db;

    public string Command => "search";

    public SearchHandler(IndexDb db) => _db = db;

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        if (!request.Args.TryGetValue("query", out var query) || string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("query is required");

        var limit = request.Args.TryGetValue("limit", out var l) && int.TryParse(l, out var n) ? n : 50;
        return await _db.SearchAsync(query, limit);
    }
}
