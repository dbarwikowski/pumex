using Pumex.Cli;

namespace Pumex.Cli.Tests;

public class DaemonLifecycleTests
{
    [Fact]
    public async Task WaitForAsync_returns_true_when_predicate_is_true_on_first_call()
    {
        var calls = 0;
        var result = await DaemonLifecycle.WaitForAsync(
            () => { calls++; return Task.FromResult(true); },
            timeout: TimeSpan.FromSeconds(1),
            interval: TimeSpan.FromMilliseconds(50));

        Assert.True(result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task WaitForAsync_returns_true_when_predicate_flips_after_some_polls()
    {
        var calls = 0;
        var result = await DaemonLifecycle.WaitForAsync(
            () => { calls++; return Task.FromResult(calls >= 3); },
            timeout: TimeSpan.FromSeconds(1),
            interval: TimeSpan.FromMilliseconds(20));

        Assert.True(result);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task WaitForAsync_returns_false_when_predicate_never_matches()
    {
        var calls = 0;
        var result = await DaemonLifecycle.WaitForAsync(
            () => { calls++; return Task.FromResult(false); },
            timeout: TimeSpan.FromMilliseconds(150),
            interval: TimeSpan.FromMilliseconds(30));

        Assert.False(result);
        Assert.True(calls >= 2, $"expected several polls within timeout, got {calls}");
    }

    [Fact]
    public async Task WaitForAsync_honors_cancellation_token()
    {
        using var cts = new CancellationTokenSource();
        var calls = 0;

        var task = DaemonLifecycle.WaitForAsync(
            () => { calls++; return Task.FromResult(false); },
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(20),
            ct: cts.Token);

        // Let the loop tick at least once before cancelling.
        await Task.Delay(50);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
    }
}
