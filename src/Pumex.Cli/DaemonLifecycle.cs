using System.Diagnostics;
using Pumex.Contracts;
using Pumex.Ipc;
using Spectre.Console;

namespace Pumex.Cli;

public static class DaemonLifecycle
{
    public static async Task<bool> WaitForAsync(
        Func<Task<bool>> predicate,
        TimeSpan timeout,
        TimeSpan interval,
        CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            if (await predicate()) return true;
            if (DateTime.UtcNow >= deadline) return false;
            await Task.Delay(interval, ct);
        }
    }

    public static Process Spawn(string daemonPath)
    {
        // Process.Start needs the working directory to exist before it spawns.
        // The daemon itself calls PumexPaths.EnsureRoot, but only after launch.
        Directory.CreateDirectory(PumexPaths.Root);

        var psi = new ProcessStartInfo
        {
            FileName = daemonPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = PumexPaths.Root,
        };
        return Process.Start(psi)
            ?? throw new InvalidOperationException("Process.Start returned null");
    }

    public static async Task<bool> IsPingingAsync(IpcClient client, int connectTimeoutMs = 500)
    {
        try
        {
            var resp = await client.SendAsync<string>("ping", connectTimeoutMs: connectTimeoutMs);
            return resp.Success;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<int> StartAsync(IpcClient client, string? daemonPath)
    {
        if (await IsPingingAsync(client))
        {
            AnsiConsole.MarkupLine("[yellow]daemon already running[/]");
            return 0;
        }

        string path;
        try
        {
            path = daemonPath is not null
                ? Path.GetFullPath(daemonPath)
                : DaemonInstaller.AutoDetectDaemonPath();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]error:[/] {ex.Message.EscapeMarkup()}");
            return 1;
        }

        if (!File.Exists(path))
        {
            AnsiConsole.MarkupLine($"[red]error:[/] daemon binary not found at {path.EscapeMarkup()} (use --daemon-path to override)");
            return 1;
        }

        try
        {
            Spawn(path);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]failed to spawn daemon:[/] {ex.Message.EscapeMarkup()}");
            return 1;
        }

        var ready = await WaitForAsync(
            () => IsPingingAsync(client, connectTimeoutMs: 200),
            timeout: TimeSpan.FromSeconds(5),
            interval: TimeSpan.FromMilliseconds(100));

        if (!ready)
        {
            AnsiConsole.MarkupLine("[red]daemon spawned but did not respond to ping within 5s[/]");
            return 1;
        }

        AnsiConsole.MarkupLine("[green]daemon started[/]");
        return 0;
    }

    public static async Task<int> StopAsync(IpcClient client)
    {
        if (!await IsPingingAsync(client))
        {
            AnsiConsole.MarkupLine("[yellow]daemon not running[/]");
            return 0;
        }

        try
        {
            var resp = await client.SendAsync<string>("stop", connectTimeoutMs: 2000);
            if (!resp.Success)
                AnsiConsole.MarkupLine($"[yellow]daemon responded with error:[/] {resp.Error?.EscapeMarkup()}");
        }
        catch (TimeoutException) { /* already gone — fine */ }
        catch (IOException) { /* daemon closed pipe before ack — fine, it's shutting down */ }

        var exited = await WaitForAsync(
            async () => !await IsPingingAsync(client, connectTimeoutMs: 100),
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(200));

        if (!exited)
        {
            AnsiConsole.MarkupLine("[red]daemon did not exit within 10s[/]");
            return 1;
        }

        AnsiConsole.MarkupLine("[green]daemon stopped[/]");
        return 0;
    }

    public static async Task<int> RestartAsync(IpcClient client, string? daemonPath)
    {
        var rc = await StopAsync(client);
        if (rc != 0) return rc;
        return await StartAsync(client, daemonPath);
    }
}
