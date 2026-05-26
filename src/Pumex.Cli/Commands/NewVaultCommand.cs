using System.CommandLine;
using System.Text.Json;
using Pumex.Contracts;
using Pumex.Ipc;
using Spectre.Console;

namespace Pumex.Cli.Commands;

internal static class NewVaultCommand
{
    private static readonly JsonSerializerOptions VaultConfigJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    internal static Command Build()
    {
        var nameArg = new Argument<string>("name") { Description = "Vault name" };
        var pathArg = new Argument<string?>("path")
        {
            Description = "Root directory (default: current directory)",
            DefaultValueFactory = _ => null,
            Arity = ArgumentArity.ZeroOrOne,
        };
        var cmd = new Command("new", "Create and register a new vault");
        cmd.Add(nameArg);
        cmd.Add(pathArg);
        cmd.SetAction(async r => await CommandHelpers.Run(c => RunAsync(c, r.GetValue(nameArg)!, r.GetValue(pathArg))));
        return cmd;
    }

    private static async Task<int> RunAsync(IpcClient client, string name, string? path)
    {
        var vaultPath = path is not null ? Path.GetFullPath(path) : Environment.CurrentDirectory;
        Directory.CreateDirectory(vaultPath);

        var markerDir = Path.Combine(vaultPath, PumexPaths.VaultMarkerDir);
        var configPath = Path.Combine(markerDir, PumexPaths.VaultConfigFile);
        if (File.Exists(configPath))
        {
            AnsiConsole.MarkupLine($"[yellow]vault already initialized at[/] {vaultPath.EscapeMarkup()}");
        }
        else
        {
            Directory.CreateDirectory(markerDir);
            var config = new VaultConfig(name, DateTimeOffset.UtcNow, VaultConfig.CurrentVersion);
            File.WriteAllText(configPath, JsonSerializer.Serialize(config, VaultConfigJson));
            AnsiConsole.MarkupLine($"[green]initialized[/] vault [bold]{name.EscapeMarkup()}[/] at {vaultPath.EscapeMarkup()}");
        }

        try
        {
            var resp = await client.SendAsync<VaultRecord>("vault:add", new()
            {
                ["name"] = name,
                ["path"] = vaultPath,
            }, connectTimeoutMs: 1500);

            if (resp.Success)
                AnsiConsole.MarkupLine($"[green]registered[/] vault [bold]{name.EscapeMarkup()}[/] with daemon");
            else
                AnsiConsole.MarkupLine($"[yellow]daemon refused registration:[/] {resp.Error?.EscapeMarkup()}");
        }
        catch (TimeoutException)
        {
            AnsiConsole.MarkupLine("[yellow]daemon not running[/] — vault marker written but not registered. Start the daemon and run [bold]pumex vault add "
                + name.EscapeMarkup() + " " + vaultPath.EscapeMarkup() + "[/] to register.");
        }

        return 0;
    }
}
