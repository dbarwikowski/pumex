using Pumex.Contracts;

namespace Pumex.Daemon.Ipc;

public class VersionHandler : ICommandHandler
{
    private readonly string _version;

    public VersionHandler(string version) => _version = version;

    public string Command => "version";

    public Task<object?> HandleAsync(IpcRequest request, CancellationToken ct) =>
        Task.FromResult<object?>(new VersionResponse(_version));
}
