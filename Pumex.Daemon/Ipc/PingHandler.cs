using Pumex.Contracts;

namespace Pumex.Daemon.Ipc;

public class PingHandler : ICommandHandler
{
    public string Command => "ping";

    public Task<object?> HandleAsync(IpcRequest request, CancellationToken ct) =>
        Task.FromResult<object?>("pong");
}
