using Pumex.Contracts;

namespace Pumex.Daemon.Ipc;

public class TagsHandler : ICommandHandler
{
    private readonly IndexDb _db;

    public string Command => "tags";

    public TagsHandler(IndexDb db) => _db = db;

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var vault = await request.ResolveVaultAsync(_db);
        return await _db.GetTagsAsync(vault?.Id);
    }
}
