using System.CommandLine;
using Pumex.Ipc;
using Spectre.Console;

namespace Pumex.Cli.Commands;

internal static class DaemonCommand
{
    internal static Command Build()
    {
        var cmd = new Command("daemon", "Manage the pumex daemon");
        cmd.Add(BuildStatus());
        cmd.Add(BuildInstall());
        cmd.Add(BuildUninstall());
        cmd.Add(BuildStart());
        cmd.Add(BuildStop());
        cmd.Add(BuildRestart());
        return cmd;
    }

    private static Command BuildStatus()
    {
        var cmd = new Command("status", "Show daemon status");
        cmd.SetAction(async _ => await CommandHelpers.Run(StatusAsync));
        return cmd;
    }

    private static Command BuildInstall()
    {
        var daemonPathOpt = new Option<string?>("--daemon-path") { Description = "Path to the daemon binary" };
        var cmd = new Command("install", "Install the daemon as a system service");
        cmd.Add(daemonPathOpt);
        cmd.SetAction(async r => await RunInstaller(r.GetValue(daemonPathOpt), i => i.InstallAsync()));
        return cmd;
    }

    private static Command BuildUninstall()
    {
        var daemonPathOpt = new Option<string?>("--daemon-path") { Description = "Path to the daemon binary" };
        var cmd = new Command("uninstall", "Uninstall the daemon");
        cmd.Add(daemonPathOpt);
        cmd.SetAction(async r => await RunInstaller(r.GetValue(daemonPathOpt), i => i.UninstallAsync()));
        return cmd;
    }

    private static Command BuildStart()
    {
        var daemonPathOpt = new Option<string?>("--daemon-path") { Description = "Path to the daemon binary" };
        var cmd = new Command("start", "Start the daemon (spawn detached, idempotent)");
        cmd.Add(daemonPathOpt);
        cmd.SetAction(async r => await DaemonLifecycle.StartAsync(new IpcClient(), r.GetValue(daemonPathOpt)));
        return cmd;
    }

    private static Command BuildStop()
    {
        var cmd = new Command("stop", "Stop the daemon gracefully via IPC");
        cmd.SetAction(async _ => await DaemonLifecycle.StopAsync(new IpcClient()));
        return cmd;
    }

    private static Command BuildRestart()
    {
        var daemonPathOpt = new Option<string?>("--daemon-path") { Description = "Path to the daemon binary" };
        var cmd = new Command("restart", "Restart the daemon (stop + start)");
        cmd.Add(daemonPathOpt);
        cmd.SetAction(async r => await DaemonLifecycle.RestartAsync(new IpcClient(), r.GetValue(daemonPathOpt)));
        return cmd;
    }

    private static async Task<int> StatusAsync(IpcClient client)
    {
        try
        {
            var resp = await client.SendAsync<string>("ping", connectTimeoutMs: 2000);
            if (resp.Success)
            {
                AnsiConsole.MarkupLine("[green]daemon is running[/]");
                return 0;
            }
            AnsiConsole.MarkupLine($"[yellow]daemon responded with error:[/] {resp.Error?.EscapeMarkup()}");
            return 1;
        }
        catch (TimeoutException)
        {
            AnsiConsole.MarkupLine("[red]daemon is not running[/]");
            return 1;
        }
    }

    private static Task<int> RunInstaller(string? daemonPath, Func<DaemonInstaller, Task<int>> action)
    {
        try
        {
            var path = daemonPath is not null
                ? Path.GetFullPath(daemonPath)
                : DaemonInstaller.AutoDetectDaemonPath();
            return action(new DaemonInstaller(path));
        }
        catch (FileNotFoundException ex)
        {
            return Task.FromResult(CommandHelpers.Error(ex.Message + " (use --daemon-path to override)"));
        }
    }
}
