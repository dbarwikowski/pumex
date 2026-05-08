using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Pumex.Daemon;

public sealed class VaultWatcher : IDisposable
{
    // Unbounded: bursty writes (git checkout, bulk rename) can flood the
    // FileSystemWatcher faster than the debounce loop drains. Dropping events
    // would leave the index stale until the next full scan; memory pressure
    // under flood is the lesser evil.
    private readonly Channel<FileEvent> _raw = Channel.CreateUnbounded<FileEvent>(
        new UnboundedChannelOptions { SingleReader = true });

    private FileSystemWatcher? _watcher;

    public void Start(string vaultPath, Action<Exception>? onError = null)
    {
        if (_watcher is not null) throw new InvalidOperationException("Watcher already started");

        _watcher = new FileSystemWatcher(vaultPath, "*.md")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite
                         | NotifyFilters.FileName
                         | NotifyFilters.DirectoryName,
        };

        _watcher.Changed += (_, e) => Enqueue(FileEventType.Changed, e.FullPath);
        _watcher.Created += (_, e) => Enqueue(FileEventType.Created, e.FullPath);
        _watcher.Deleted += (_, e) => Enqueue(FileEventType.Deleted, e.FullPath);
        _watcher.Renamed += (_, e) =>
        {
            Enqueue(FileEventType.Deleted, e.OldFullPath);
            Enqueue(FileEventType.Created, e.FullPath);
        };
        // Vault root deleted/moved/permissions-revoked, or kernel buffer overflow.
        // Forward the exception, then complete the channel so ReadBatchesAsync
        // returns cleanly and the IndexingService loop exits without crashing.
        _watcher.Error += (_, e) =>
        {
            onError?.Invoke(e.GetException());
            _raw.Writer.TryComplete();
        };

        _watcher.EnableRaisingEvents = true;
    }

    private void Enqueue(FileEventType type, string path) =>
        _raw.Writer.TryWrite(new FileEvent(type, path));

    // Debounce: editors save the same file 3-5 times per Ctrl+S. Collect
    // events for 200 ms after the first one, dedup by path (last event wins),
    // then emit the batch.
    public async IAsyncEnumerable<IReadOnlyList<FileEvent>> ReadBatchesAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var pending = new Dictionary<string, FileEvent>(StringComparer.OrdinalIgnoreCase);

        while (await _raw.Reader.WaitToReadAsync(ct))
        {
            while (_raw.Reader.TryRead(out var evt))
                pending[evt.Path] = evt;

            try { await Task.Delay(200, ct); }
            catch (OperationCanceledException) { yield break; }

            while (_raw.Reader.TryRead(out var evt))
                pending[evt.Path] = evt;

            if (pending.Count == 0) continue;

            var batch = pending.Values.ToArray();
            pending.Clear();
            yield return batch;
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
        _raw.Writer.TryComplete();
    }
}
