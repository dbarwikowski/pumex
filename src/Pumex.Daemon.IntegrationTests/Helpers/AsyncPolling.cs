namespace Pumex.Daemon.IntegrationTests.Helpers;

internal static class AsyncPolling
{
    /// <summary>
    /// Polls <paramref name="predicate"/> every <paramref name="intervalMs"/>
    /// until it returns true or <paramref name="timeoutMs"/> elapses. Used for
    /// asserting state that changes asynchronously (watcher debounce, background
    /// indexing, etc.). Throws Xunit.Sdk.XunitException on timeout.
    /// </summary>
    public static async Task UntilAsync(
        Func<Task<bool>> predicate,
        int timeoutMs = 5000,
        int intervalMs = 50,
        string? message = null)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate()) return;
            await Task.Delay(intervalMs);
        }
        throw new Xunit.Sdk.XunitException(message ?? $"Condition not met within {timeoutMs}ms");
    }

    public static Task UntilAsync(
        Func<bool> predicate,
        int timeoutMs = 5000,
        int intervalMs = 50,
        string? message = null)
        => UntilAsync(() => Task.FromResult(predicate()), timeoutMs, intervalMs, message);
}
