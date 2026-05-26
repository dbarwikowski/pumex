using Pumex.Contracts;

namespace Pumex.Daemon.Ipc;

public class BacklinksHandler : ICommandHandler
{
    private readonly IVaultRepository _vaults;
    private readonly INoteRepository _notes;
    private readonly ILinkRepository _links;

    public string Command => "backlinks";

    public BacklinksHandler(IVaultRepository vaults, INoteRepository notes, ILinkRepository links)
    {
        _vaults = vaults;
        _notes = notes;
        _links = links;
    }

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var vault = await request.ResolveVaultAsync(_vaults);
        var path = await IpcRequestExtensions.ResolveNotePathAsync(request.Require("path"), vault, _notes);
        return await _links.GetBacklinksAsync(path, vault?.Id);
    }
}
