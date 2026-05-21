using Microsoft.Extensions.Hosting;
using Pumex.Contracts;

namespace Pumex.Daemon.Ipc;

public class StopHandler : ICommandHandler
{
    private readonly IHostApplicationLifetime _lifetime;

    public StopHandler(IHostApplicationLifetime lifetime) => _lifetime = lifetime;

    public string Command => "stop";

    public Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        _lifetime.StopApplication();
        return Task.FromResult<object?>("ok");
    }
}
