using Pumex.Contracts;

namespace Pumex.Daemon.Ipc;

public class TagsHandler : ICommandHandler
{
    private readonly IndexDb _db;

    public string Command => "tags";

    public TagsHandler(IndexDb db) => _db = db;

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct) =>
        await _db.GetTagsAsync();
}
