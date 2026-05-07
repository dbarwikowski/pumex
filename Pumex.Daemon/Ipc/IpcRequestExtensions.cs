using Pumex.Contracts;

namespace Pumex.Daemon.Ipc;

internal static class IpcRequestExtensions
{
    public static string Require(this IpcRequest request, string name)
    {
        if (!request.Args.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{name} is required");
        return value;
    }

    public static string? Optional(this IpcRequest request, string name) =>
        request.Args.TryGetValue(name, out var value) ? value : null;

    public static bool Flag(this IpcRequest request, string name) =>
        request.Args.TryGetValue(name, out var value) && value is "1" or "true" or "yes";
}
