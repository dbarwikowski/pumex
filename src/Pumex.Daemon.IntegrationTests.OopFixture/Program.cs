using System.Text.Json.Nodes;
using Pumex.Contracts;
using Pumex.Plugin.Sdk;

// Used by Pumex.Daemon.IntegrationTests to exercise the 001B auto-spawn path
// end-to-end. The daemon launches this exe with PUMEX_DAEMON_PIPE /
// PUMEX_PLUGIN_PIPE / PUMEX_PLUGIN_NAME set; the SDK handles the handshake.

await PumexPluginHost.RunAsync(new OopFixturePlugin());

internal sealed class OopFixturePlugin : PumexPlugin
{
    public override Task<IReadOnlyList<IPluginCommandHandler>> OnInitAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<IPluginCommandHandler>>(
            [new EchoHandler(), new VaultsCallbackHandler(this)]);

    private sealed class EchoHandler : IPluginCommandHandler
    {
        public string Command => "oop:echo";

        public Task<JsonNode?> HandleAsync(IpcRequest request, CancellationToken ct)
        {
            var msg = request.Args.TryGetValue("msg", out var v) ? v : "";
            return Task.FromResult<JsonNode?>(JsonValue.Create(msg));
        }
    }

    // Reentrancy probe: the plugin calls back into the daemon while handling
    // a request from the daemon. The daemon's IpcServer fans out per-connection,
    // so this works — the test asserts it does.
    private sealed class VaultsCallbackHandler : IPluginCommandHandler
    {
        private readonly OopFixturePlugin _plugin;

        public VaultsCallbackHandler(OopFixturePlugin plugin) => _plugin = plugin;

        public string Command => "oop:vaults";

        public async Task<JsonNode?> HandleAsync(IpcRequest request, CancellationToken ct)
        {
            var vaults = await _plugin.Context.Host.GetVaultsAsync(ct);
            var arr = new JsonArray();
            foreach (var v in vaults) arr.Add(JsonValue.Create(v.Name));
            return arr;
        }
    }
}
