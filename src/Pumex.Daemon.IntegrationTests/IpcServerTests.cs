using Microsoft.Extensions.Logging.Abstractions;
using Pumex.Contracts;
using Pumex.Daemon.Ipc;
using Pumex.Daemon.IntegrationTests.Helpers;

namespace Pumex.Daemon.IntegrationTests;

[Collection("ipc-server")]
public class IpcServerTests
{
    [Fact]
    public async Task Ping_returns_pong_over_the_pipe()
    {
        await using var run = await IpcServerRun.StartAsync(handlers => handlers.Add(new PingHandler()));

        var resp = await run.Client.SendAsync<string>("ping");

        Assert.True(resp.Success);
        Assert.Equal("pong", resp.Data);
    }

    [Fact]
    public async Task Unknown_command_returns_failure_response()
    {
        await using var run = await IpcServerRun.StartAsync(_ => { });

        var resp = await run.Client.SendAsync<object>("does-not-exist");

        Assert.False(resp.Success);
        Assert.Contains("does-not-exist", resp.Error);
    }

    [Fact]
    public async Task Search_handler_returns_results_after_upserting_a_note()
    {
        using var fixture = await TestVault.CreateAsync();
        await using var run = await IpcServerRun.StartAsync(
            handlers => handlers.Add(new SearchHandler(fixture.Vaults, fixture.Search)));

        var note = new NoteDocument(
            Path: Path.Combine(fixture.Root, "n.md"),
            Frontmatter: new Dictionary<string, object>(),
            Tags: [],
            OutgoingLinks: [],
            Content: "ipc test marker word: marsupial",
            RawContent: "ipc test marker word: marsupial",
            Mtime: 1, Size: 32);
        // Snippet builder reads from disk, so the file must actually exist.
        File.WriteAllText(note.Path, note.Content);
        await fixture.UpsertAsync(fixture.Vault.Id, [note]);

        var resp = await run.Client.SendAsync<List<SearchResult>>("search", new()
        {
            ["query"] = "marsupial",
            ["vaultPath"] = fixture.Vault.Path,
        });

        Assert.True(resp.Success);
        Assert.Single(resp.Data!);
        Assert.Equal(note.Path, resp.Data![0].Path);
    }

    [Fact]
    public async Task Context_handler_returns_a_passage_and_pointer_over_the_pipe()
    {
        using var fixture = await TestVault.CreateAsync();
        var path = fixture.WriteNote("indexing.md",
            "# Indexing\n\nThe indexer handles config changes by reloading the policy and rescanning.\n");
        fixture.WriteNote("cats.md", "# Cats\n\nUnrelated content about naps.\n");
        await fixture.UpsertAsync(fixture.Vault.Id, [
            ParseFrom(path),
            ParseFrom(Path.Combine(fixture.Root, "cats.md")),
        ]);

        await using var run = await IpcServerRun.StartAsync(
            handlers => handlers.Add(new ContextHandler(fixture.Vaults, new ContextRepository(fixture.Context))));

        var resp = await run.Client.SendAsync<List<ContextResult>>("context", new()
        {
            ["query"] = "how does the indexer handle config changes",
            ["vaultPath"] = fixture.Vault.Path,
        });

        Assert.True(resp.Success, resp.Error);
        var hit = Assert.Single(resp.Data!);
        Assert.Equal("indexing.md", hit.RelativePath);
        Assert.Equal("indexing", hit.Pointer);          // unique bare name
        Assert.Contains("indexer handles config changes", hit.Passage);
    }

    [Fact]
    public async Task Note_read_resolves_bare_name_via_the_index()
    {
        using var fixture = await TestVault.CreateAsync();
        // Two notes — the bare name "today" must pick the right one.
        var todayPath = fixture.WriteNote("today.md", "# today\n\nhello world\n");
        fixture.WriteNote("yesterday.md", "# yesterday\n");
        await fixture.UpsertAsync(fixture.Vault.Id, [
            ParseFrom(todayPath),
            ParseFrom(Path.Combine(fixture.Root, "yesterday.md")),
        ]);

        await using var run = await IpcServerRun.StartAsync(
            handlers => handlers.Add(new NoteReadHandler(FormatParserRegistry.Default(), fixture.Vaults, fixture.Notes)));

        var resp = await run.Client.SendAsync<NoteContent>("note:read", new()
        {
            ["path"] = "today",                // bare name, no separator, no extension
            ["vaultPath"] = fixture.Vault.Path,
        });

        Assert.True(resp.Success, resp.Error);
        Assert.Equal(todayPath, resp.Data!.Path);
    }

    [Fact]
    public async Task Note_read_list_property_renders_as_comma_separated()
    {
        using var fixture = await TestVault.CreateAsync();
        var path = fixture.WriteNote("aliases.md", "---\naliases: [render test, fm test]\n---\n\nbody\n");
        await fixture.UpsertAsync(fixture.Vault.Id, [ParseFrom(path)]);

        await using var run = await IpcServerRun.StartAsync(
            handlers => handlers.Add(new NoteReadHandler(FormatParserRegistry.Default(), fixture.Vaults, fixture.Notes)));

        var resp = await run.Client.SendAsync<NoteContent>("note:read", new()
        {
            ["path"] = path,
            ["vaultPath"] = fixture.Vault.Path,
        });

        Assert.True(resp.Success, resp.Error);
        Assert.Equal("render test, fm test", resp.Data!.Properties["aliases"]);
    }

    [Fact]
    public async Task Note_read_tags_property_is_omitted_from_properties_table()
    {
        using var fixture = await TestVault.CreateAsync();
        var path = fixture.WriteNote("tagged.md", "---\ntags: [test, pumex]\ntitle: Hello\n---\n\nbody\n");
        await fixture.UpsertAsync(fixture.Vault.Id, [ParseFrom(path)]);

        await using var run = await IpcServerRun.StartAsync(
            handlers => handlers.Add(new NoteReadHandler(FormatParserRegistry.Default(), fixture.Vaults, fixture.Notes)));

        var resp = await run.Client.SendAsync<NoteContent>("note:read", new()
        {
            ["path"] = path,
            ["vaultPath"] = fixture.Vault.Path,
        });

        Assert.True(resp.Success, resp.Error);
        Assert.DoesNotContain("tags", resp.Data!.Properties.Keys);
        Assert.Equal("Hello", resp.Data!.Properties["title"]);
    }

    [Fact]
    public async Task Note_read_nested_object_property_renders_as_key_value_pairs()
    {
        using var fixture = await TestVault.CreateAsync();
        var path = fixture.WriteNote("nested.md", "---\nmeta:\n  author: Alice\n  labels: [a, b]\n---\n\nbody\n");
        await fixture.UpsertAsync(fixture.Vault.Id, [ParseFrom(path)]);

        await using var run = await IpcServerRun.StartAsync(
            handlers => handlers.Add(new NoteReadHandler(FormatParserRegistry.Default(), fixture.Vaults, fixture.Notes)));

        var resp = await run.Client.SendAsync<NoteContent>("note:read", new()
        {
            ["path"] = path,
            ["vaultPath"] = fixture.Vault.Path,
        });

        Assert.True(resp.Success, resp.Error);
        Assert.Equal("author: Alice, labels: a, b", resp.Data!.Properties["meta"]);
    }

    [Fact]
    public async Task Note_read_empty_list_property_renders_as_empty_string()
    {
        using var fixture = await TestVault.CreateAsync();
        var path = fixture.WriteNote("emptylist.md", "---\naliases: []\n---\n\nbody\n");
        await fixture.UpsertAsync(fixture.Vault.Id, [ParseFrom(path)]);

        await using var run = await IpcServerRun.StartAsync(
            handlers => handlers.Add(new NoteReadHandler(FormatParserRegistry.Default(), fixture.Vaults, fixture.Notes)));

        var resp = await run.Client.SendAsync<NoteContent>("note:read", new()
        {
            ["path"] = path,
            ["vaultPath"] = fixture.Vault.Path,
        });

        Assert.True(resp.Success, resp.Error);
        Assert.Equal("", resp.Data!.Properties["aliases"]);
    }

    [Fact]
    public async Task Property_get_resolves_a_json_file_by_filename_and_returns_a_top_level_scalar()
    {
        using var fixture = await TestVault.CreateAsync();
        var path = fixture.WriteNote("data/settings.json", """{ "theme": "dark", "nested": { "x": 1 } }""");
        await fixture.UpsertAsync(fixture.Vault.Id, [new JsonFormatParser().Parse(path)]);

        await using var run = await IpcServerRun.StartAsync(
            handlers => handlers.Add(new PropertyGetHandler(fixture.Vaults, fixture.Notes)));

        var resp = await run.Client.SendAsync<string>("property:get", new()
        {
            ["path"] = "settings.json",   // explicit extension, resolved by filename (non-MD, read-only)
            ["key"] = "theme",
            ["vaultPath"] = fixture.Vault.Path,
        });

        Assert.True(resp.Success, resp.Error);
        Assert.Equal("dark", resp.Data);
    }

    [Fact]
    public async Task Property_list_on_a_json_file_omits_nested_values()
    {
        using var fixture = await TestVault.CreateAsync();
        var path = fixture.WriteNote("data/settings.json", """{ "theme": "dark", "nested": { "x": 1 } }""");
        await fixture.UpsertAsync(fixture.Vault.Id, [new JsonFormatParser().Parse(path)]);

        await using var run = await IpcServerRun.StartAsync(
            handlers => handlers.Add(new PropertyListHandler(fixture.Vaults, fixture.Notes)));

        var resp = await run.Client.SendAsync<List<PropertyEntry>>("property:list", new()
        {
            ["path"] = "settings.json",
            ["vaultPath"] = fixture.Vault.Path,
        });

        Assert.True(resp.Success, resp.Error);
        Assert.Equal("dark", resp.Data!.Single(p => p.Key == "theme").Value);
        Assert.DoesNotContain(resp.Data!, p => p.Key == "nested");   // nested object is not a top-level scalar
    }

    private static NoteDocument ParseFrom(string path) => new NoteParser().Parse(path);

    private sealed class IpcServerRun : IAsyncDisposable
    {
        public TestIpcClient Client { get; }
        private readonly IpcServer _server;
        private readonly Task _task;
        private readonly CancellationTokenSource _cts;

        private IpcServerRun(IpcServer server, Task task, CancellationTokenSource cts, TestIpcClient client)
        {
            _server = server;
            _task = task;
            _cts = cts;
            Client = client;
        }

        public static async Task<IpcServerRun> StartAsync(Action<List<ICommandHandler>> configureHandlers)
        {
            var pipeName = "pumex-test-" + Guid.NewGuid().ToString("N");
            var handlers = new List<ICommandHandler>();
            configureHandlers(handlers);

            var server = new IpcServer(handlers, NullLogger<IpcServer>.Instance, pipeName);
            var cts = new CancellationTokenSource();
            await server.StartAsync(cts.Token);
            // BackgroundService.StartAsync hands control back as soon as ExecuteAsync hits its first await.
            // The pipe server registers on the first WaitForConnectionAsync, so we give it a beat.
            await Task.Delay(50);
            return new IpcServerRun(server, Task.CompletedTask, cts, new TestIpcClient(pipeName));
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try { await _server.StopAsync(CancellationToken.None); } catch { }
            _cts.Dispose();
        }
    }
}
