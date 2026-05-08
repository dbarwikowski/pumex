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
            async () => (await fixture.Db.GetAllPathsAsync(fixture.Vault.Id)).Count == 2);

        var tags = await fixture.Db.GetTagsAsync(fixture.Vault.Id);
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
        await AsyncPolling.UntilAsync(async () => (await fixture.Db.GetAllPathsAsync(fixture.Vault.Id)).Count >= 1);

        // Newly created note must propagate through the watcher.
        fixture.WriteNote("fresh.md", "added at runtime, contains marker word: capybara\n");

        await AsyncPolling.UntilAsync(
            async () =>
            {
                var hits = await fixture.Db.SearchAsync("capybara", vaultId: fixture.Vault.Id);
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
                var backlinks = await fixture.Db.GetBacklinksAsync(targetPath, vaultId: fixture.Vault.Id);
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
        await AsyncPolling.UntilAsync(async () => (await fixture.Db.GetAllPathsAsync(fixture.Vault.Id)).Count == 2);

        File.Delete(doomed);

        await AsyncPolling.UntilAsync(
            async () => (await fixture.Db.GetAllPathsAsync(fixture.Vault.Id)).Count == 1,
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
                fixture.Vault, fixture.Db, new NoteParser(), new WikilinkResolver(), new VaultWatcher(),
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
