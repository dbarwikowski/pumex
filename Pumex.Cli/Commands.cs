using System.Text.Json;
using Pumex.Contracts;
using Spectre.Console;

namespace Pumex.Cli;

public static class Commands
{
    private static readonly JsonSerializerOptions VaultConfigJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static async Task<int> NewVaultAsync(IpcClient client, string[] args)
    {
        if (args.Length == 0) return Usage("pumex new <name> [path]");

        var name = args[0];
        var vaultPath = args.Length >= 2 ? Path.GetFullPath(args[1]) : Environment.CurrentDirectory;

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

        // Try to register with the daemon. If it's not running, the marker is
        // written and the user can register later.
        try
        {
            var resp = await client.SendAsync<VaultRecord>("vault:add", new()
            {
                ["name"] = name,
                ["path"] = vaultPath,
            }, connectTimeoutMs: 1500);

            if (resp.Success)
                AnsiConsole.MarkupLine("[green]registered[/] with daemon");
            else
                AnsiConsole.MarkupLine($"[yellow]daemon refused registration:[/] {resp.Error?.EscapeMarkup()}");
        }
        catch (TimeoutException)
        {
            AnsiConsole.MarkupLine("[yellow]daemon not running[/] — vault marker written but not registered. Start the daemon and run [bold]pumex vault add " + name.EscapeMarkup() + " " + vaultPath.EscapeMarkup() + "[/] to register.");
        }

        return 0;
    }

    public static async Task<int> PingAsync(IpcClient client)
    {
        var resp = await client.SendAsync<string>("ping");
        if (!resp.Success) return Error(resp.Error);

        AnsiConsole.MarkupLine($"[green]{resp.Data?.EscapeMarkup() ?? "ok"}[/]");
        return 0;
    }

    public static async Task<int> SearchAsync(IpcClient client, string[] args)
    {
        var (scope, rest) = VaultArgs.Extract(args);
        if (rest.Length == 0) return Usage("pumex search <query> [--limit N] [--vault NAME | --vault-path PATH | --all]");

        var query = rest[0];
        var requestArgs = new Dictionary<string, string> { ["query"] = query };
        scope.ApplyTo(requestArgs);

        for (var i = 1; i < rest.Length - 1; i++)
        {
            if (rest[i] == "--limit") requestArgs["limit"] = rest[i + 1];
        }

        var resp = await client.SendAsync<List<SearchResult>>("search", requestArgs);
        if (!resp.Success) return Error(resp.Error);

        var results = resp.Data ?? [];
        if (results.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]no matches[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Minimal);
        table.AddColumn("Note");
        table.AddColumn("Path");
        table.AddColumn("Snippet");
        foreach (var r in results)
            table.AddRow(r.Name.EscapeMarkup(), r.Path.EscapeMarkup(), r.Snippet.EscapeMarkup());

        AnsiConsole.Write(table);
        return 0;
    }

    public static async Task<int> TagsAsync(IpcClient client, string[] args)
    {
        var (scope, _) = VaultArgs.Extract(args);
        var requestArgs = new Dictionary<string, string>();
        scope.ApplyTo(requestArgs);

        var resp = await client.SendAsync<List<TagCount>>("tags", requestArgs);
        if (!resp.Success) return Error(resp.Error);

        var tags = resp.Data ?? [];
        if (tags.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]no tags[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Minimal);
        table.AddColumn("Tag");
        table.AddColumn(new TableColumn("Count").RightAligned());
        foreach (var t in tags)
            table.AddRow($"#{t.Tag.EscapeMarkup()}", t.Count.ToString());

        AnsiConsole.Write(table);
        return 0;
    }

    public static async Task<int> BacklinksAsync(IpcClient client, string[] args)
    {
        var (scope, rest) = VaultArgs.Extract(args);
        if (rest.Length == 0) return Usage("pumex backlinks <path> [--vault NAME | --vault-path PATH | --all]");

        var requestArgs = new Dictionary<string, string> { ["path"] = VaultArgs.ResolvePath(scope, rest[0]) };
        scope.ApplyTo(requestArgs);

        var resp = await client.SendAsync<List<string>>("backlinks", requestArgs);
        if (!resp.Success) return Error(resp.Error);

        var paths = resp.Data ?? [];
        if (paths.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]no backlinks[/]");
            return 0;
        }

        foreach (var p in paths) AnsiConsole.WriteLine(p);
        return 0;
    }

    public static async Task<int> VaultsAsync(IpcClient client)
    {
        var resp = await client.SendAsync<List<VaultRecord>>("vaults");
        if (!resp.Success) return Error(resp.Error);

        var vaults = resp.Data ?? [];
        if (vaults.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]no vaults registered. Add one with[/] [yellow]pumex vault add <name> <path>[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Minimal);
        table.AddColumn("Name");
        table.AddColumn("Path");
        foreach (var v in vaults)
            table.AddRow(v.Name.EscapeMarkup(), v.Path.EscapeMarkup());

        AnsiConsole.Write(table);
        return 0;
    }

    public static async Task<int> VaultAsync(IpcClient client, string[] args)
    {
        if (args.Length < 1) return Usage("pumex vault add <name> <path>");

        return args[0] switch
        {
            "add" => await VaultAddAsync(client, args[1..]),
            _ => Usage("pumex vault add <name> <path>"),
        };
    }

    private static async Task<int> VaultAddAsync(IpcClient client, string[] args)
    {
        if (args.Length < 2) return Usage("pumex vault add <name> <path>");

        var name = args[0];
        var path = Path.GetFullPath(args[1]);

        var resp = await client.SendAsync<VaultRecord>("vault:add", new()
        {
            ["name"] = name,
            ["path"] = path,
        });
        if (!resp.Success) return Error(resp.Error);

        var v = resp.Data!;
        AnsiConsole.MarkupLine($"[green]added[/] vault [bold]{v.Name.EscapeMarkup()}[/] at {v.Path.EscapeMarkup()}");
        return 0;
    }

    public static async Task<int> NoteAsync(IpcClient client, string[] args)
    {
        if (args.Length == 0) return Usage("pumex note <read|create|append> ...");

        return args[0] switch
        {
            "read" => await NoteReadAsync(client, args[1..]),
            "create" => await NoteWriteAsync(client, args[1..], "note:create"),
            "append" => await NoteWriteAsync(client, args[1..], "note:append"),
            _ => Usage("pumex note <read|create|append> ..."),
        };
    }

    private static async Task<int> NoteReadAsync(IpcClient client, string[] args)
    {
        var (scope, rest) = VaultArgs.Extract(args);
        if (rest.Length == 0) return Usage("pumex note read <path> [--raw] [--vault NAME | --vault-path PATH]");

        var path = VaultArgs.ResolvePath(scope, rest[0]);
        var raw = rest.Contains("--raw");

        var requestArgs = new Dictionary<string, string> { ["path"] = path };
        scope.ApplyTo(requestArgs);

        var resp = await client.SendAsync<NoteContent>("note:read", requestArgs);
        if (!resp.Success) return Error(resp.Error);

        var note = resp.Data!;
        if (raw)
        {
            AnsiConsole.WriteLine(note.Raw);
            return 0;
        }

        if (note.Properties.Count > 0)
        {
            var table = new Table().Border(TableBorder.Minimal);
            table.AddColumn("Property");
            table.AddColumn("Value");
            foreach (var (k, v) in note.Properties)
                table.AddRow(k.EscapeMarkup(), v.EscapeMarkup());
            AnsiConsole.Write(table);
        }
        if (note.Tags.Count > 0)
            AnsiConsole.MarkupLine("[dim]tags:[/] " + string.Join(" ", note.Tags.Select(t => $"[blue]#{t.EscapeMarkup()}[/]")));
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine(note.Body);
        return 0;
    }

    private static async Task<int> NoteWriteAsync(IpcClient client, string[] args, string command)
    {
        if (args.Length == 0) return Usage($"pumex note {command[5..]} <path> [--content TEXT | --stdin] [--inline]");

        var path = Path.GetFullPath(args[0]);
        string? content = null;
        var inline = false;
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--content" when i + 1 < args.Length:
                    content = args[++i];
                    break;
                case "--stdin":
                    content = await Console.In.ReadToEndAsync();
                    break;
                case "--inline":
                    inline = true;
                    break;
            }
        }

        if (content is null && !Console.IsInputRedirected)
        {
            AnsiConsole.MarkupLine("[yellow]error:[/] no content. Use [bold]--content TEXT[/] or pipe via [bold]--stdin[/].");
            return 64;
        }
        content ??= await Console.In.ReadToEndAsync();

        var requestArgs = new Dictionary<string, string>
        {
            ["path"] = path,
            ["content"] = content,
        };
        if (inline) requestArgs["inline"] = "true";

        var resp = await client.SendAsync<NotePathResult>(command, requestArgs);
        if (!resp.Success) return Error(resp.Error);

        AnsiConsole.MarkupLine($"[green]{(command == "note:create" ? "created" : "appended")}[/] {resp.Data!.Path.EscapeMarkup()}");
        return 0;
    }

    public static async Task<int> DaemonAsync(IpcClient client, string[] args)
    {
        if (args.Length == 0) return Usage("pumex daemon <status|install|uninstall|restart> [--daemon-path PATH]");

        return args[0] switch
        {
            "status" => await DaemonStatusAsync(client),
            "install" => await DaemonInstallAsync(args[1..], install: true),
            "uninstall" => await DaemonInstallAsync(args[1..], install: false),
            "restart" => await DaemonRestartAsync(args[1..]),
            _ => Usage("pumex daemon <status|install|uninstall|restart>"),
        };
    }

    private static Task<int> DaemonInstallAsync(string[] args, bool install)
    {
        var daemonPath = ParseDaemonPath(args);
        try
        {
            var installer = new DaemonInstaller(daemonPath);
            return install ? installer.InstallAsync() : installer.UninstallAsync();
        }
        catch (FileNotFoundException ex)
        {
            return Task.FromResult(Error(ex.Message + " (use --daemon-path to override)"));
        }
    }

    private static Task<int> DaemonRestartAsync(string[] args)
    {
        var daemonPath = ParseDaemonPath(args);
        try
        {
            return new DaemonInstaller(daemonPath).RestartAsync();
        }
        catch (FileNotFoundException ex)
        {
            return Task.FromResult(Error(ex.Message + " (use --daemon-path to override)"));
        }
    }

    private static string ParseDaemonPath(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--daemon-path") return Path.GetFullPath(args[i + 1]);
        }
        return DaemonInstaller.AutoDetectDaemonPath();
    }

    private static async Task<int> DaemonStatusAsync(IpcClient client)
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

    private static int Usage(string line)
    {
        AnsiConsole.MarkupLine($"[yellow]usage:[/] {line.EscapeMarkup()}");
        return 64;
    }

    private static int Error(string? message)
    {
        AnsiConsole.MarkupLine($"[red]error:[/] {(message ?? "unknown error").EscapeMarkup()}");
        return 1;
    }
}
