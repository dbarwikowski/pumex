using Microsoft.Extensions.Hosting;
using Pumex.Contracts;
using Pumex.Daemon.Ipc;

namespace Pumex.Daemon.Tests;

public class StopHandlerTests
{
    [Fact]
    public void Command_is_stop()
    {
        var handler = new StopHandler(new FakeLifetime());
        Assert.Equal("stop", handler.Command);
    }

    [Fact]
    public async Task HandleAsync_signals_host_shutdown_and_returns_ok()
    {
        var lifetime = new FakeLifetime();
        var handler = new StopHandler(lifetime);

        var result = await handler.HandleAsync(new IpcRequest("stop", new Dictionary<string, string>()), CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.Equal(1, lifetime.StopApplicationCallCount);
    }

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();

        public int StopApplicationCallCount { get; private set; }

        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopped.Token;

        public void StopApplication()
        {
            StopApplicationCallCount++;
            _stopping.Cancel();
        }
    }
}
