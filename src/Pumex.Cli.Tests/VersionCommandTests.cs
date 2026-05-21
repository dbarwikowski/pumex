using Pumex.Cli;

namespace Pumex.Cli.Tests;

public class VersionCommandTests
{
    [Fact]
    public void FormatVersion_renders_cli_and_daemon_when_both_known()
    {
        var (cli, daemon) = VersionCommand.FormatVersion("0.1.0", "0.1.0");

        Assert.Equal("pumex 0.1.0", cli);
        Assert.Equal("pumex-daemon 0.1.0", daemon);
    }

    [Fact]
    public void FormatVersion_marks_daemon_not_running_when_version_is_null()
    {
        var (cli, daemon) = VersionCommand.FormatVersion("0.1.0", null);

        Assert.Equal("pumex 0.1.0", cli);
        Assert.Equal("pumex-daemon (not running)", daemon);
    }

    [Fact]
    public void FormatVersion_prints_both_versions_on_mismatch_with_no_warning()
    {
        var (cli, daemon) = VersionCommand.FormatVersion("0.1.0", "0.2.0");

        Assert.Equal("pumex 0.1.0", cli);
        Assert.Equal("pumex-daemon 0.2.0", daemon);
    }

    [Fact]
    public void FormatVersion_passes_unknown_through_for_cli_side()
    {
        var (cli, daemon) = VersionCommand.FormatVersion("unknown", "0.1.0");

        Assert.Equal("pumex unknown", cli);
        Assert.Equal("pumex-daemon 0.1.0", daemon);
    }
}
