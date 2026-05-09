using Pumex.Contracts;

namespace Pumex.Daemon.Ipc;

public interface ICommandHandler
{
    string Command { get; }
    Task<object?> HandleAsync(IpcRequest request, CancellationToken ct);
}
