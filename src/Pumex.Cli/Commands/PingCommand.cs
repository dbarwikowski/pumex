using System.CommandLine;
using Pumex.Ipc;
using Spectre.Console;

namespace Pumex.Cli.Commands;

internal static class PingCommand
{
    internal static Command Build()
    {
        var cmd = new Command("ping", "Check that the daemon is running");
        cmd.SetAction(async _ => await CommandHelpers.Run(RunAsync));
        return cmd;
    }

    private static async Task<int> RunAsync(IpcClient client)
    {
        var resp = await client.SendAsync<string>("ping");
        if (!resp.Success) return CommandHelpers.Error(resp.Error);
        AnsiConsole.MarkupLine($"[green]{resp.Data?.EscapeMarkup() ?? "ok"}[/]");
        return 0;
    }
}
