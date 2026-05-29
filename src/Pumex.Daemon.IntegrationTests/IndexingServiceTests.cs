using Microsoft.Extensions.Logging.Abstractions;
using Pumex.Daemon.IntegrationTests.Helpers;

namespace Pumex.Daemon.IntegrationTests;

public class IndexingServiceTests
{
    [Fact]
    public async Task Initial_full_scan_indexes_existing_notes()
    {
        using var fixture = await TestVault.CreateAsync();
        fixture.WriteNote("alpha.md", "---\ntitle: A\n---\n\nbody #shared\n");
        fixture.WriteNote("beta.md",  "no frontmatter, just body #shared\n");

        await using var run = await IndexingRun.StartAsync(fixture);

        await AsyncPolling.UntilAsync(
            async () => (await fixture.Notes.GetAllPathsAsync(fixture.Vault.Id)).Count == 2);

        var tags = await fixture.Vaults.GetTagsAsync(fixture.Vault.Id);
        Assert.Single(tags);
        Assert.Equal("shared", tags[0].Tag);
        Assert.Equal(2, tags[0].Count);
    }

    [Fact]
    public async Task Watcher_picks_up_new_files_after_initial_scan()
    {
        using var fixture = await TestVault.CreateAsync();
        fixture.WriteNote("seed.md", "seed body\n");
        await using var run = await IndexingRun.StartAsync(fixture);
        await AsyncPolling.UntilAsync(async () => (await fixture.Notes.GetAllPathsAsync(fixture.Vault.Id)).Count >= 1);

        // Newly created note must propagate through the watcher.
        fixture.WriteNote("fresh.md", "added at runtime, contains marker word: capybara\n");

        await AsyncPolling.UntilAsync(
            async () =>
            {
                var hits = await fixture.Search.SearchAsync("capybara", vaultId: fixture.Vault.Id);
                return hits.Any(h => h.Path.EndsWith("fresh.md"));
            },
            timeoutMs: 8000);
    }

    [Fact]
    public async Task Wikilinks_resolve_and_show_up_as_backlinks()
    {
        using var fixture = await TestVault.CreateAsync();
        fixture.WriteNote("source.md", "see [[target]]\n");
        fixture.WriteNote("target.md", "the target\n");

        await using var run = await IndexingRun.StartAsync(fixture);

        await AsyncPolling.UntilAsync(
            async () =>
            {
                var sourcePath = Path.Combine(fixture.Root, "source.md");
                var targetPath = Path.Combine(fixture.Root, "target.md");
                var backlinks = await fixture.Links.GetBacklinksAsync(targetPath, vaultId: fixture.Vault.Id);
                return backlinks.Any(b => string.Equals(b, sourcePath, StringComparison.OrdinalIgnoreCase));
            });
    }

    [Fact]
    public async Task Deleted_files_are_removed_from_the_index()
    {
        using var fixture = await TestVault.CreateAsync();
        var doomed = fixture.WriteNote("doomed.md", "body\n");
        fixture.WriteNote("survivor.md", "body\n");

        await using var run = await IndexingRun.StartAsync(fixture);
        await AsyncPolling.UntilAsync(async () => (await fixture.Notes.GetAllPathsAsync(fixture.Vault.Id)).Count == 2);

        File.Delete(doomed);

        await AsyncPolling.UntilAsync(
            async () => (await fixture.Notes.GetAllPathsAsync(fixture.Vault.Id)).Count == 1,
            timeoutMs: 8000);
    }

    [Fact]
    public async Task Configured_extra_formats_are_indexed()
    {
        using var fixture = await TestVault.CreateAsync();
        fixture.WriteConfig(formats: ["csv"]);
        fixture.WriteNote("data.csv", "id,animal\n1,capybara\n");

        await using var run = await IndexingRun.StartAsync(fixture);

        await AsyncPolling.UntilAsync(
            async () =>
            {
                var hits = await fixture.Search.SearchAsync("capybara", vaultId: fixture.Vault.Id);
                return hits.Any(h => h.Path.EndsWith("data.csv"));
            },
            timeoutMs: 8000);
    }

    [Fact]
    public async Task Non_markdown_files_are_ignored_without_config()
    {
        using var fixture = await TestVault.CreateAsync();
        fixture.WriteNote("note.md", "markdown body\n");
        fixture.WriteNote("data.csv", "id,animal\n1,capybara\n");

        await using var run = await IndexingRun.StartAsync(fixture);
        await AsyncPolling.UntilAsync(async () => (await fixture.Notes.GetAllPathsAsync(fixture.Vault.Id)).Count == 1);

        var paths = await fixture.Notes.GetAllPathsAsync(fixture.Vault.Id);
        Assert.DoesNotContain(paths, p => p.EndsWith("data.csv"));
    }

    [Fact]
    public async Task Disabling_a_format_de_indexes_its_files()
    {
        using var fixture = await TestVault.CreateAsync();
        fixture.WriteConfig(formats: ["csv"]);
        fixture.WriteNote("data.csv", "id,animal\n1,capybara\n");

        await using var run = await IndexingRun.StartAsync(fixture);
        await AsyncPolling.UntilAsync(async () => (await fixture.Notes.GetAllPathsAsync(fixture.Vault.Id)).Count == 1);

        // Drop csv from the active set; the live config watch must re-scan and purge it.
        fixture.WriteConfig();

        await AsyncPolling.UntilAsync(
            async () => (await fixture.Notes.GetAllPathsAsync(fixture.Vault.Id)).Count == 0,
            timeoutMs: 8000);
    }

    [Fact]
    public async Task Ignore_globs_exclude_matching_files()
    {
        using var fixture = await TestVault.CreateAsync();
        fixture.WriteConfig(ignore: ["ignored/**"]);
        fixture.WriteNote("keep.md", "keep me\n");
        fixture.WriteNote(Path.Combine("ignored", "skip.md"), "skip me\n");

        await using var run = await IndexingRun.StartAsync(fixture);
        await AsyncPolling.UntilAsync(async () => (await fixture.Notes.GetAllPathsAsync(fixture.Vault.Id)).Count == 1);

        var paths = await fixture.Notes.GetAllPathsAsync(fixture.Vault.Id);
        Assert.Contains(paths, p => p.EndsWith("keep.md"));
        Assert.DoesNotContain(paths, p => p.EndsWith("skip.md"));
    }

    [Fact]
    public async Task Explicit_extension_wikilink_to_non_markdown_resolves_as_backlink()
    {
        using var fixture = await TestVault.CreateAsync();
        fixture.WriteConfig(formats: ["csv"]);
        fixture.WriteNote("source.md", "see [[data.csv]]\n");
        fixture.WriteNote("data.csv", "id,animal\n1,capybara\n");

        await using var run = await IndexingRun.StartAsync(fixture);

        await AsyncPolling.UntilAsync(
            async () =>
            {
                var sourcePath = Path.Combine(fixture.Root, "source.md");
                var targetPath = Path.Combine(fixture.Root, "data.csv");
                var backlinks = await fixture.Links.GetBacklinksAsync(targetPath, vaultId: fixture.Vault.Id);
                return backlinks.Any(b => string.Equals(b, sourcePath, StringComparison.OrdinalIgnoreCase));
            },
            timeoutMs: 8000);
    }

    /// <summary>
    /// Helper that wires up an <see cref="IndexingService"/> and runs it on a
    /// background task. Disposing cancels the run and waits for the task to
    /// settle so the <see cref="VaultWatcher"/> releases its handles before
    /// the temp dir is deleted.
    /// </summary>
    private sealed class IndexingRun : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly IndexingService _service;
        private readonly Task _task;

        private IndexingRun(IndexingService service, Task task, CancellationTokenSource cts)
        {
            _service = service;
            _task = task;
            _cts = cts;
        }

        public static Task<IndexingRun> StartAsync(TestVault fixture)
        {
            var service = new IndexingService(
                fixture.Vault,
                fixture.Context,
                fixture.Notes,
                fixture.Links,
                FormatParserRegistry.Default(),
                new WikilinkResolver(),
                new VaultWatcher(),
                NullLogger<IndexingService>.Instance);
            var cts = new CancellationTokenSource();
            var task = Task.Run(async () =>
            {
                try { await service.RunAsync(cts.Token); }
                catch (OperationCanceledException) { }
            });
            return Task.FromResult(new IndexingRun(service, task, cts));
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try { await _task.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch { /* swallow during teardown */ }
            _service.Dispose();
            _cts.Dispose();
        }
    }
}
