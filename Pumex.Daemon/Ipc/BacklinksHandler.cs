using Pumex.Contracts;

namespace Pumex.Daemon.Ipc;

public class BacklinksHandler : ICommandHandler
{
    private readonly IndexDb _db;

    public string Command => "backlinks";

    public BacklinksHandler(IndexDb db) => _db = db;

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        if (!request.Args.TryGetValue("path", out var path) || string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("path is required");

        return await _db.GetBacklinksAsync(path);
    }
}
