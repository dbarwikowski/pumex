using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pumex.Contracts;
using Pumex.Daemon.Ipc;
using Pumex.Daemon.IntegrationTests.Helpers;
using Pumex.Daemon.Plugins;
using Pumex.Plugin.Sdk;

namespace Pumex.Daemon.IntegrationTests;

[Collection("ipc-server")]
public class OutOfProcessPluginTests
{
    // Tests 1–3 use a stand-in PluginIpcServer running in-process on a side
    // pipe — exercises the daemon's proxy code path without paying for a
    // subprocess spawn. The auto-spawn end-to-end test is below in
    // Auto_spawn_lifecycle_round_trips_a_real_subprocess.

    [Fact]
    public async Task Proxy_dispatches_to_a_registered_out_of_proc_plugin()
    {
        var pluginPipe = "pumex-test-oopplug-" + Guid.NewGuid().ToString("N");

        var pluginCts = new CancellationTokenSource();
        var pluginServer = new PluginIpcServer(pluginPipe, [new EchoHandler()]);
        var pluginTask = pluginServer.RunAsync(pluginCts.Token);

        try
        {
            await Task.Delay(50);

            await using var run = await OopRun.StartAsync(registry =>
                registry.RegisterOutOfProcess("echo-plug", "1.0", pluginPipe, ["oop:echo"]));

            var resp = await run.Client.SendAsync<string>("oop:echo", new() { ["msg"] = "round-trip" });

            Assert.True(resp.Success, resp.Error);
            Assert.Equal("round-trip", resp.Data);
        }
        finally
        {
            pluginCts.Cancel();
            try { await pluginTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task Plugin_dead_pipe_unregisters_and_returns_clean_error()
    {
        // Register a pipe nobody listens on. The proxy should time out, mark
        // the plugin dead, and a subsequent plugin:list call should not show it.
        var deadPipe = "pumex-test-dead-" + Guid.NewGuid().ToString("N");

        await using var run = await OopRun.StartAsync(registry =>
            registry.RegisterOutOfProcess("dead-plug", "1.0", deadPipe, ["dead:cmd"]));

        var resp = await run.Client.SendAsync<object>("dead:cmd");

        Assert.False(resp.Success);
        Assert.Contains("dead-plug", resp.Error);

        // MarkDead should have evicted it from the registry; plugin:list reflects.
        var list = await run.Client.SendAsync<List<PluginInfo>>("plugin:list");
        Assert.True(list.Success, list.Error);
        Assert.DoesNotContain(list.Data!, p => p.Name == "dead-plug");
    }

    [Fact]
    public async Task Reentrant_call_from_plugin_back_into_daemon_does_not_deadlock()
    {
        // Plugin handler calls host.GetVaultsAsync against the daemon's own
        // pipe. The daemon dispatches per-connection, so this nested request
        // doesn't block on the outer one.
        var pluginPipe = "pumex-test-reentrant-" + Guid.NewGuid().ToString("N");

        var pluginCts = new CancellationTokenSource();
        await using var run = await OopRun.StartAsync(_ => { });

        var client = new Pumex.Ipc.IpcClient(run.PipeName);
        var host = new OutOfProcessPumexHost(client);
        var pluginServer = new PluginIpcServer(pluginPipe, [new ReentrantVaultsHandler(host)]);
        var pluginTask = pluginServer.RunAsync(pluginCts.Token);

        try
        {
            await Task.Delay(50);
            run.Registry.RegisterOutOfProcess("reentrant-plug", "1.0", pluginPipe, ["oop:vaults"]);

            // The daemon's VaultsHandler is wired through OopRun; vaults list
            // is empty (no vaults registered in the test DB). What we're really
            // asserting is that the nested call completes — not what it returns.
            var resp = await run.Client.SendAsync<JsonElement>("oop:vaults");

            Assert.True(resp.Success, resp.Error);
            Assert.Equal(JsonValueKind.Array, resp.Data.ValueKind);
        }
        finally
        {
            pluginCts.Cancel();
            try { await pluginTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task Plugin_list_includes_in_proc_and_out_of_proc_entries()
    {
        var pluginPipe = "pumex-test-oopplug-list-" + Guid.NewGuid().ToString("N");

        await using var run = await OopRun.StartAsync(registry =>
        {
            registry.Register("inproc-plug", new InProcStub("inproc:do"));
            registry.RegisterOutOfProcess("outproc-plug", "0.3", pluginPipe, ["outproc:do"]);
        });

        var resp = await run.Client.SendAsync<List<PluginInfo>>("plugin:list");

        Assert.True(resp.Success, resp.Error);
        var byName = resp.Data!.ToDictionary(p => p.Name);
        Assert.Equal("in-process", byName["inproc-plug"].Kind);
        Assert.Equal("out-of-process", byName["outproc-plug"].Kind);
        Assert.Equal("0.3", byName["outproc-plug"].Version);
        Assert.Equal(pluginPipe, byName["outproc-plug"].Pipe);
    }

    [Fact]
    public async Task Auto_spawn_lifecycle_round_trips_a_real_subprocess()
    {
        var fixtureDir = Path.Combine(AppContext.BaseDirectory, "oop-fixture");
        var executableName = OperatingSystem.IsWindows()
            ? "Pumex.Daemon.IntegrationTests.OopFixture.exe"
            : "Pumex.Daemon.IntegrationTests.OopFixture";
        Assert.True(
            File.Exists(Path.Combine(fixtureDir, executableName)),
            $"OopFixture exe missing — expected at {fixtureDir}. Did the CopyOopFixtureOutputs target run?");

        // Build a scratch plugins dir that mirrors the on-disk layout the
        // loader expects: <plugins>/<name>/manifest.json + the executable's
        // files alongside.
        var scratch = Path.Combine(Path.GetTempPath(), "pumex-oop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var pluginDir = Path.Combine(scratch, "oop-fixture");
            CopyDirectory(fixtureDir, pluginDir);

            var manifest = new PluginManifest(
                SchemaVersion: 1,
                Name: "oop-fixture",
                Version: "0.1.0",
                Executable: executableName);
            await File.WriteAllTextAsync(
                Path.Combine(pluginDir, "manifest.json"),
                JsonSerializer.Serialize(manifest, PumexPluginJsonContext.Default.PluginManifest));

            // Bring up an IpcServer + PluginLoader pointed at the scratch dir.
            // The loader spawns the fixture exe, which calls plugin:register
            // back over the daemon's pipe.
            await using var run = await OopRun.StartAsync(_ => { }, pluginsRoot: scratch);

            // Wait until the plugin has registered itself — handshake takes a
            // beat because the child has to spin up the .NET runtime.
            await AsyncPolling.UntilAsync(async () =>
            {
                var list = await run.Client.SendAsync<List<PluginInfo>>("plugin:list");
                return list.Success
                    && list.Data!.Any(p => p.Name == "oop-fixture" && p.Commands.Contains("oop:echo"));
            }, timeoutMs: 15_000, intervalMs: 100,
               message: "plugin process did not register within 15s");

            var resp = await run.Client.SendAsync<string>("oop:echo", new() { ["msg"] = "via-subprocess" });
            Assert.True(resp.Success, resp.Error);
            Assert.Equal("via-subprocess", resp.Data);
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); } catch { }
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.EnumerateDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }

    private sealed class EchoHandler : IPluginCommandHandler
    {
        public string Command => "oop:echo";
        public Task<JsonNode?> HandleAsync(IpcRequest request, CancellationToken ct)
        {
            var msg = request.Args.TryGetValue("msg", out var v) ? v : "";
            return Task.FromResult<JsonNode?>(JsonValue.Create(msg));
        }
    }

    private sealed class ReentrantVaultsHandler : IPluginCommandHandler
    {
        private readonly IPumexHost _host;
        public ReentrantVaultsHandler(IPumexHost host) => _host = host;
        public string Command => "oop:vaults";
        public async Task<JsonNode?> HandleAsync(IpcRequest request, CancellationToken ct)
        {
            var vaults = await _host.GetVaultsAsync(ct);
            var arr = new JsonArray();
            foreach (var v in vaults) arr.Add(JsonValue.Create(v.Name));
            return arr;
        }
    }

    private sealed class InProcStub : IPluginCommandHandler
    {
        public InProcStub(string command) => Command = command;
        public string Command { get; }
        public Task<JsonNode?> HandleAsync(IpcRequest request, CancellationToken ct)
            => Task.FromResult<JsonNode?>(null);
    }

    private sealed class NullPumexHost : IPumexHost
    {
        public Task<IReadOnlyList<VaultRecord>> GetVaultsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<VaultRecord>>([]);
        public Task<VaultRecord?> ResolveVaultAsync(string nameOrPath, CancellationToken ct = default)
            => Task.FromResult<VaultRecord?>(null);
        public Task<IReadOnlyList<SearchResult>> SearchAsync(
            string? query, long? vaultId, int limit,
            IReadOnlyList<string>? tags, IReadOnlyList<KeyValuePair<string, string>>? properties,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SearchResult>>([]);
        public Task<NoteContent> ReadNoteAsync(string path, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<TagCount>> GetTagsAsync(long? vaultId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TagCount>>([]);
    }

    // Test host: spins up an IpcServer + the 001B plugin control-plane
    // handlers, and lets the caller seed the registry however it wants.
    private sealed class OopRun : IAsyncDisposable
    {
        public TestIpcClient Client { get; }
        public PluginRegistry Registry { get; }
        public string PipeName { get; }

        private readonly PluginLoader _loader;
        private readonly IpcServer _server;
        private readonly CancellationTokenSource _cts;
        private readonly ServiceProvider _sp;

        private OopRun(
            PluginLoader loader,
            IpcServer server,
            CancellationTokenSource cts,
            TestIpcClient client,
            ServiceProvider sp,
            PluginRegistry registry,
            string pipeName)
        {
            _loader = loader;
            _server = server;
            _cts = cts;
            Client = client;
            _sp = sp;
            Registry = registry;
            PipeName = pipeName;
        }

        public static async Task<OopRun> StartAsync(Action<PluginRegistry> seed, string? pluginsRoot = null)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<PluginRegistry>();
            services.AddSingleton<IPumexHost>(new NullPumexHost());
            var sp = services.BuildServiceProvider();

            var registry = sp.GetRequiredService<PluginRegistry>();
            seed(registry);

            var pipeName = "pumex-test-oop-" + Guid.NewGuid().ToString("N");
            var handlers = new List<ICommandHandler>
            {
                new PluginRegisterHandler(registry),
                new PluginUnregisterHandler(registry),
                new PluginListHandler(registry),
                new VaultsHandler(MakeNoOpIndexDb()),
            };

            var server = new IpcServer(
                handlers: handlers,
                logger: NullLogger<IpcServer>.Instance,
                pipeName: pipeName,
                plugins: registry);

            var cts = new CancellationTokenSource();
            await server.StartAsync(cts.Token);
            // Server must be accepting before the loader spawns plugins —
            // otherwise the plugin's plugin:register call hits a dead pipe.
            await Task.Delay(50);

            // Either point the loader at the caller's plugins dir (auto-spawn
            // path) or at a non-existent dir (in-proc seeded-by-test path).
            var loaderRoot = pluginsRoot ?? Path.Combine(Path.GetTempPath(),
                "pumex-no-plugins-" + Guid.NewGuid().ToString("N"));
            var loader = new PluginLoader(
                registry,
                sp,
                sp.GetRequiredService<ILogger<PluginLoader>>(),
                pluginsRoot: loaderRoot,
                daemonPipeName: pipeName);
            await loader.StartAsync(CancellationToken.None);

            return new OopRun(loader, server, cts, new TestIpcClient(pipeName), sp, registry, pipeName);
        }

        private static IndexDb MakeNoOpIndexDb()
        {
            // The reentrancy test needs `vaults` to succeed (it doesn't care
            // about the payload). A temp-file SQLite DB is the simplest way to
            // get a real IndexDb that responds without standing up a fixture.
            var dbPath = Path.Combine(Path.GetTempPath(), "pumex-oop-test-" + Guid.NewGuid().ToString("N") + ".db");
            return new IndexDb(dbPath);
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try { await _server.StopAsync(CancellationToken.None); } catch { }
            try { await _loader.StopAsync(CancellationToken.None); } catch { }
            _cts.Dispose();
            await _sp.DisposeAsync();
        }
    }
}
