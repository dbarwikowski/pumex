using System.Text.Json.Nodes;
using Pumex.Contracts;

namespace Pumex.Plugin.Sdk;

public interface IPluginCommandHandler
{
    string Command { get; }

    // Returns a JsonNode the daemon will inline as IpcResponse.Data. Plugins
    // serialise via their OWN JsonSerializerContext — this keeps the daemon
    // AOT-friendly without registering every plugin's response shape in
    // PumexJsonContext.
    Task<JsonNode?> HandleAsync(IpcRequest request, CancellationToken ct);
}
