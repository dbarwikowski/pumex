using Pumex.Contracts;
using Pumex.Daemon.Ipc;

namespace Pumex.Daemon.Tests;

public class VersionHandlerTests
{
    [Fact]
    public void Command_is_version()
    {
        Assert.Equal("version", new VersionHandler("0.1.0").Command);
    }

    [Fact]
    public async Task HandleAsync_returns_the_configured_version()
    {
        var handler = new VersionHandler("0.1.0");

        var result = await handler.HandleAsync(new IpcRequest("version", new Dictionary<string, string>()), CancellationToken.None);

        var response = Assert.IsType<VersionResponse>(result);
        Assert.Equal("0.1.0", response.Version);
    }

    [Fact]
    public async Task HandleAsync_passes_arbitrary_version_strings_through()
    {
        var handler = new VersionHandler("9.9.9-rc.1");

        var result = await handler.HandleAsync(new IpcRequest("version", new Dictionary<string, string>()), CancellationToken.None);

        var response = Assert.IsType<VersionResponse>(result);
        Assert.Equal("9.9.9-rc.1", response.Version);
    }
}
