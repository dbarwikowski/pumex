using Microsoft.Extensions.Hosting;

namespace Pumex.Plugin.Sdk;

// Author-facing base class. The daemon's loader sets Context before OnInitAsync
// fires. Authors implement OnInitAsync (return handlers), optionally override
// ExecuteAsync for background work (a periodic scan, an external watcher, etc.).
public abstract class PumexPlugin : BackgroundService
{
    public PluginContext Context { get; private set; } = null!;

    internal void Bind(PluginContext context) => Context = context;

    public abstract Task<IReadOnlyList<IPluginCommandHandler>> OnInitAsync(CancellationToken ct);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.Delay(Timeout.Infinite, stoppingToken);
}
