using Pumex.Contracts;
using Pumex.Ipc;
using Spectre.Console;

namespace Pumex.Cli;

public static class VersionCommand
{
    public static async Task<int> RunAsync(IpcClient client, string cliVersion)
    {
        var daemonVersion = await TryGetDaemonVersionAsync(client);
        var (cli, daemon) = FormatVersion(cliVersion, daemonVersion);
        AnsiConsole.WriteLine(cli);
        AnsiConsole.WriteLine(daemon);
        return 0;
    }

    public static (string Cli, string Daemon) FormatVersion(string cliVersion, string? daemonVersion) =>
        ($"pumex {cliVersion}",
         $"pumex-daemon {daemonVersion ?? "(not running)"}");

    private static async Task<string?> TryGetDaemonVersionAsync(IpcClient client)
    {
        try
        {
            var resp = await client.SendAsync<VersionResponse>("version", connectTimeoutMs: 500);
            return resp.Success ? resp.Data?.Version : null;
        }
        catch
        {
            return null;
        }
    }
}
