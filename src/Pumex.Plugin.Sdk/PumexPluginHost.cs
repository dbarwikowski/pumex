using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pumex.Ipc;

namespace Pumex.Plugin.Sdk;

// One-call entry point for out-of-proc plugins. A plugin author's Program.cs
// boils down to:
//
//     await PumexPluginHost.RunAsync(new MyPlugin());
//
// The host reads the daemon-supplied environment, builds an OutOfProcessPumexHost
// so the plugin can call back into the daemon, calls the plugin's OnInitAsync
// to get its handlers, registers them with the daemon via plugin:register, and
// then runs the plugin's own IpcServer-lite on PUMEX_PLUGIN_PIPE.
public static class PumexPluginHost
{
    public static async Task RunAsync(
        PumexPlugin plugin,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        logger ??= NullLogger.Instance;

        var name = Environment.GetEnvironmentVariable("PUMEX_PLUGIN_NAME")
            ?? throw new InvalidOperationException("PUMEX_PLUGIN_NAME not set; this binary must be launched by the Pumex daemon.");
        var pipeName = Environment.GetEnvironmentVariable("PUMEX_PLUGIN_PIPE")
            ?? throw new InvalidOperationException("PUMEX_PLUGIN_PIPE not set; this binary must be launched by the Pumex daemon.");
        var daemonPipe = Environment.GetEnvironmentVariable("PUMEX_DAEMON_PIPE")
            ?? "pumex-daemon";
        var dataDir = Environment.GetEnvironmentVariable("PUMEX_PLUGIN_DATA")
            ?? Directory.GetCurrentDirectory();

        var client = new IpcClient(daemonPipe);
        var host = new OutOfProcessPumexHost(client);

        // Stub manifest — out-of-proc plugins typically don't see their full
        // manifest.json at runtime (the daemon read it to spawn them). We
        // surface what we know via env, and authors who need richer metadata
        // can read manifest.json themselves from PUMEX_PLUGIN_DATA's parent.
        var manifest = new PluginManifest(
            SchemaVersion: 1,
            Name: name,
            Version: "0.0.0");

        plugin.Bind(new PluginContext(manifest, host, dataDir, logger));

        var handlers = await plugin.OnInitAsync(ct);

        // Start the plugin's pipe server FIRST, then register with the daemon.
        // The daemon's proxy connects to our pipe per-request, so the listener
        // has to be up before the daemon can route anything to us.
        var server = new PluginIpcServer(pipeName, handlers, logger);
        var serverTask = server.RunAsync(ct);

        // Give the accept loop a beat to bind (mirrors the test harness pattern).
        await Task.Delay(50, ct);

        await client.SendAsync<object>("plugin:register", new()
        {
            ["name"] = name,
            ["version"] = manifest.Version,
            ["pipe"] = pipeName,
            ["commands"] = string.Join(',', handlers.Select(h => h.Command)),
        }, ct: ct);

        // Run the plugin's BackgroundService alongside the accept loop. We have
        // to wait on plugin.ExecuteTask (the long-running task returned from
        // ExecuteAsync) rather than plugin.StartAsync — the latter completes
        // as soon as ExecuteAsync hits its first await, which would end the
        // host prematurely.
        await plugin.StartAsync(ct);
        var pluginTask = plugin.ExecuteTask ?? Task.Delay(Timeout.Infinite, ct);
        await Task.WhenAny(serverTask, pluginTask);

        // Best-effort: tell the daemon we're going away so it can drop our
        // commands from the dispatch table without waiting for a proxy timeout.
        try
        {
            await client.SendAsync<object>("plugin:unregister", new() { ["name"] = name }, ct: CancellationToken.None);
        }
        catch { /* daemon may be down */ }

        try { await plugin.StopAsync(CancellationToken.None); } catch { }
    }
}
