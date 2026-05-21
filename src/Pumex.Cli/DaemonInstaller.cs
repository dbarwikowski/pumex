using System.Diagnostics;
using System.Security;
using System.Text;
using Pumex.Contracts;
using Spectre.Console;

namespace Pumex.Cli;

public class DaemonInstaller
{
    private const string ServiceName = "pumex";
    private const string MacLabel = "com.pumex.daemon";
    private const string WindowsTaskName = "Pumex Daemon";

    private readonly string _daemonPath;

    public DaemonInstaller(string daemonPath)
    {
        if (!File.Exists(daemonPath))
            throw new FileNotFoundException($"Daemon binary not found at {daemonPath}");
        _daemonPath = daemonPath;
    }

    public static string AutoDetectDaemonPath()
    {
        var cliDir = Path.GetDirectoryName(Environment.ProcessPath)
            ?? throw new InvalidOperationException("Cannot determine CLI directory");
        var name = OperatingSystem.IsWindows() ? "pumex-daemon.exe" : "pumex-daemon";
        return Path.Combine(cliDir, name);
    }

    public Task<int> InstallAsync() =>
        OperatingSystem.IsWindows() ? InstallWindowsAsync()
        : OperatingSystem.IsMacOS() ? InstallMacAsync()
        : InstallLinuxAsync();

    public Task<int> UninstallAsync() =>
        OperatingSystem.IsWindows() ? UninstallWindowsAsync()
        : OperatingSystem.IsMacOS() ? UninstallMacAsync()
        : UninstallLinuxAsync();

    // -------------------- Linux (systemd --user) --------------------

    private static string LinuxUnitPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config/systemd/user/pumex.service");

    private async Task<int> InstallLinuxAsync()
    {
        var unit = $"""
            [Unit]
            Description=Pumex Daemon
            After=default.target

            [Service]
            Type=notify
            ExecStart={_daemonPath}
            Restart=on-failure
            RestartSec=5

            [Install]
            WantedBy=default.target
            """;

        Directory.CreateDirectory(Path.GetDirectoryName(LinuxUnitPath)!);
        await File.WriteAllTextAsync(LinuxUnitPath, unit);

        if (await Run("systemctl", "--user", "daemon-reload") != 0) return 1;
        if (await Run("systemctl", "--user", "enable", "--now", ServiceName) != 0) return 1;

        AnsiConsole.MarkupLine($"[green]installed[/] systemd user service: {LinuxUnitPath.EscapeMarkup()}");
        return 0;
    }

    private async Task<int> UninstallLinuxAsync()
    {
        await Run("systemctl", "--user", "disable", "--now", ServiceName);
        if (File.Exists(LinuxUnitPath)) File.Delete(LinuxUnitPath);
        await Run("systemctl", "--user", "daemon-reload");

        AnsiConsole.MarkupLine("[green]uninstalled[/] systemd user service");
        return 0;
    }

    // -------------------- Windows (schtasks) --------------------

    // XML form gives us explicit principal (LeastPrivilege, no admin) and a working
    // directory under $HOME/.pumex — schtasks /create with /tr alone has neither.
    private async Task<int> InstallWindowsAsync()
    {
        var xml = BuildWindowsTaskXml(_daemonPath, PumexPaths.Root);
        var xmlPath = Path.Combine(Path.GetTempPath(), $"pumex-task-{Guid.NewGuid():N}.xml");
        try
        {
            // schtasks expects UTF-16 with BOM.
            await File.WriteAllTextAsync(xmlPath, xml, Encoding.Unicode);

            if (await Run("schtasks", "/create", "/tn", WindowsTaskName, "/xml", xmlPath, "/f") != 0)
                return 1;

            if (await Run("schtasks", "/run", "/tn", WindowsTaskName) != 0)
                AnsiConsole.MarkupLine("[yellow]warning:[/] task created but failed to start now. Try [bold]pumex daemon start[/].");

            AnsiConsole.MarkupLine($"[green]installed[/] scheduled task [bold]{WindowsTaskName}[/]");
            return 0;
        }
        finally
        {
            try { File.Delete(xmlPath); } catch { /* best effort */ }
        }
    }

    private async Task<int> UninstallWindowsAsync()
    {
        await Run("schtasks", "/end", "/tn", WindowsTaskName);
        await Run("schtasks", "/delete", "/tn", WindowsTaskName, "/f");
        AnsiConsole.MarkupLine("[green]uninstalled[/] scheduled task");
        return 0;
    }

    internal static string BuildWindowsTaskXml(string daemonPath, string workingDir)
    {
        var userId = $@"{Environment.UserDomainName}\{Environment.UserName}";
        var cmd = SecurityElement.Escape(daemonPath);
        var wd = SecurityElement.Escape(workingDir);
        var uid = SecurityElement.Escape(userId);
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Pumex knowledge engine daemon</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{uid}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{uid}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <StartWhenAvailable>true</StartWhenAvailable>
                <AllowHardTerminate>true</AllowHardTerminate>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{cmd}</Command>
                  <WorkingDirectory>{wd}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    // -------------------- macOS (launchd) --------------------

    private static string MacPlistPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        $"Library/LaunchAgents/{MacLabel}.plist");

    private async Task<int> InstallMacAsync()
    {
        var plist = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
                "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>Label</key>
                <string>{MacLabel}</string>
                <key>ProgramArguments</key>
                <array>
                    <string>{_daemonPath}</string>
                </array>
                <key>RunAtLoad</key>
                <true/>
                <key>KeepAlive</key>
                <true/>
            </dict>
            </plist>
            """;

        Directory.CreateDirectory(Path.GetDirectoryName(MacPlistPath)!);
        await File.WriteAllTextAsync(MacPlistPath, plist);

        if (await Run("launchctl", "load", MacPlistPath) != 0) return 1;
        AnsiConsole.MarkupLine($"[green]installed[/] launchd agent: {MacPlistPath.EscapeMarkup()}");
        return 0;
    }

    private async Task<int> UninstallMacAsync()
    {
        if (File.Exists(MacPlistPath))
        {
            await Run("launchctl", "unload", MacPlistPath);
            File.Delete(MacPlistPath);
        }
        AnsiConsole.MarkupLine("[green]uninstalled[/] launchd agent");
        return 0;
    }

    // -------------------- helpers --------------------

    private static async Task<int> Run(string file, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)!;
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (proc.ExitCode != 0)
        {
            var msg = (stderr + stdout).Trim();
            var argv = string.Join(' ', args).EscapeMarkup();
            if (msg.Length > 0)
                AnsiConsole.MarkupLine($"[red]{file} {argv} (exit {proc.ExitCode}):[/]\n{msg.EscapeMarkup()}");
            else
                AnsiConsole.MarkupLine($"[red]{file} {argv} failed with exit {proc.ExitCode}[/]");
        }
        return proc.ExitCode;
    }
}
