using Pumex.Contracts;

namespace Pumex.Daemon.Ipc;

public class TagsHandler : ICommandHandler
{
    private readonly IVaultRepository _vaults;

    public string Command => "tags";

    public TagsHandler(IVaultRepository vaults) => _vaults = vaults;

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        var vault = await request.ResolveVaultAsync(_vaults);
        return await _vaults.GetTagsAsync(vault?.Id);
    }
}
