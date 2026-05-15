using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pumex.Contracts;
using Pumex.Daemon.IntegrationTests.Helpers;
using Pumex.Daemon.Plugins;
using Pumex.Plugin.Sdk;

namespace Pumex.Daemon.IntegrationTests;

[Collection("ipc-server")]
public class PluginLoaderTests
{
    [Fact]
    public async Task Loaded_plugin_command_round_trips_through_ipc_server()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "pumex-plug-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            await SetUpFixturePluginAsync(scratch);

            await using var run = await PluginRun.StartAsync(scratch);

            var resp = await run.Client.SendAsync<string>("test:echo", new() { ["msg"] = "hello" });

            Assert.True(resp.Success, resp.Error);
            Assert.Equal("hello", resp.Data);
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task Missing_plugins_root_is_a_noop()
    {
        var nonExistent = Path.Combine(Path.GetTempPath(), "pumex-plug-missing-" + Guid.NewGuid().ToString("N"));

        await using var run = await PluginRun.StartAsync(nonExistent);

        var resp = await run.Client.SendAsync<string>("test:echo");
        Assert.False(resp.Success);
        Assert.Contains("test:echo", resp.Error);
    }

    [Fact]
    public async Task Bad_manifest_is_skipped_and_does_not_block_other_plugins()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "pumex-plug-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            // One broken plugin directory + one healthy fixture plugin alongside.
            var brokenDir = Path.Combine(scratch, "broken");
            Directory.CreateDirectory(brokenDir);
            await File.WriteAllTextAsync(Path.Combine(brokenDir, "manifest.json"), "{ not json");

            await SetUpFixturePluginAsync(scratch);

            await using var run = await PluginRun.StartAsync(scratch);

            var resp = await run.Client.SendAsync<string>("test:echo", new() { ["msg"] = "still alive" });
            Assert.True(resp.Success, resp.Error);
            Assert.Equal("still alive", resp.Data);
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); } catch { /* best-effort */ }
        }
    }

    private static async Task SetUpFixturePluginAsync(string pluginsRoot)
    {
        var pluginDir = Path.Combine(pluginsRoot, "test-fixture");
        Directory.CreateDirectory(pluginDir);

        var asm = typeof(EchoFixturePlugin).Assembly;
        var asmFile = Path.GetFileName(asm.Location);
        File.Copy(asm.Location, Path.Combine(pluginDir, asmFile), overwrite: true);

        var manifest = new PluginManifest(
            SchemaVersion: 1,
            Name: "test-fixture",
            Version: "0.1.0",
            EntryAssembly: asmFile,
            EntryType: typeof(EchoFixturePlugin).FullName!);

        await File.WriteAllTextAsync(
            Path.Combine(pluginDir, "manifest.json"),
            JsonSerializer.Serialize(manifest, PumexPluginJsonContext.Default.PluginManifest));
    }

    public sealed class EchoFixturePlugin : PumexPlugin
    {
        public override Task<IReadOnlyList<IPluginCommandHandler>> OnInitAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<IPluginCommandHandler>>([new EchoHandler()]);

        private sealed class EchoHandler : IPluginCommandHandler
        {
            public string Command => "test:echo";

            public Task<JsonNode?> HandleAsync(IpcRequest request, CancellationToken ct)
            {
                var msg = request.Args.TryGetValue("msg", out var m) ? m : "";
                return Task.FromResult<JsonNode?>(JsonValue.Create(msg));
            }
        }
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

    private sealed class PluginRun : IAsyncDisposable
    {
        public TestIpcClient Client { get; }

        private readonly PluginLoader _loader;
        private readonly IpcServer _server;
        private readonly CancellationTokenSource _cts;
        private readonly ServiceProvider _sp;

        private PluginRun(PluginLoader loader, IpcServer server, CancellationTokenSource cts, TestIpcClient client, ServiceProvider sp)
        {
            _loader = loader;
            _server = server;
            _cts = cts;
            Client = client;
            _sp = sp;
        }

        public static async Task<PluginRun> StartAsync(string pluginsRoot)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<PluginRegistry>();
            services.AddSingleton<IPumexHost>(new NullPumexHost());
            var sp = services.BuildServiceProvider();

            var registry = sp.GetRequiredService<PluginRegistry>();
            var loader = new PluginLoader(
                registry,
                sp,
                sp.GetRequiredService<ILogger<PluginLoader>>(),
                pluginsRoot: pluginsRoot);
            await loader.StartAsync(CancellationToken.None);

            var pipeName = "pumex-test-plugin-" + Guid.NewGuid().ToString("N");
            var server = new IpcServer(
                handlers: [],
                logger: NullLogger<IpcServer>.Instance,
                pipeName: pipeName,
                plugins: registry);

            var cts = new CancellationTokenSource();
            await server.StartAsync(cts.Token);
            // Same pattern as IpcServerTests — give the accept loop a beat to bind.
            await Task.Delay(50);

            return new PluginRun(loader, server, cts, new TestIpcClient(pipeName), sp);
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
