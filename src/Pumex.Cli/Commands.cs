using System.Text.Json;
using Pumex.Contracts;
using Pumex.Ipc;
using Spectre.Console;

namespace Pumex.Cli;

public static class Commands
{
    private static readonly JsonSerializerOptions VaultConfigJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static async Task<int> PingAsync(IpcClient client)
    {
        var resp = await client.SendAsync<string>("ping");
        if (!resp.Success) return Error(resp.Error);
        AnsiConsole.MarkupLine($"[green]{resp.Data?.EscapeMarkup() ?? "ok"}[/]");
        return 0;
    }

    public static async Task<int> NewVaultAsync(IpcClient client, string name, string? path)
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

    public static async Task<int> SearchAsync(IpcClient client, string? query, string[] tags, string[] properties, int? limit, VaultScope scope)
    {
        var expandedTags = ExpandTags(tags).ToList();
        var expandedProps = ExpandProperties(properties).ToList();

        if (query is null && expandedTags.Count == 0 && expandedProps.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]usage:[/] pumex search <query> [--tag X] [--property k=v] [--limit N] [--vault ...]");
            return 64;
        }

        var requestArgs = new Dictionary<string, string>();
        if (query is not null) requestArgs["query"] = query;
        if (limit is not null) requestArgs["limit"] = limit.Value.ToString();
        if (expandedTags.Count > 0) requestArgs["tags"] = string.Join(',', expandedTags);
        if (expandedProps.Count > 0) requestArgs["properties"] = string.Join(';', expandedProps);
        scope.ApplyTo(requestArgs);

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

    public static async Task<int> TagsAsync(IpcClient client, VaultScope scope)
    {
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

    public static async Task<int> BacklinksAsync(IpcClient client, string note, VaultScope scope)
    {
        var requestArgs = new Dictionary<string, string> { ["path"] = VaultArgs.ResolvePath(scope, note) };
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

    public static async Task<int> VaultListAsync(IpcClient client)
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

    public static async Task<int> VaultAddAsync(IpcClient client, string name, string path)
    {
        var resp = await client.SendAsync<VaultRecord>("vault:add", new()
        {
            ["name"] = name,
            ["path"] = Path.GetFullPath(path),
        });
        if (!resp.Success) return Error(resp.Error);

        var v = resp.Data!;
        AnsiConsole.MarkupLine($"[green]added[/] vault [bold]{v.Name.EscapeMarkup()}[/] at {v.Path.EscapeMarkup()}");
        return 0;
    }

    public static async Task<int> VaultRemoveAsync(IpcClient client, string name)
    {
        var resp = await client.SendAsync<VaultRecord>("vault:remove", new() { ["name"] = name });
        if (!resp.Success) return Error(resp.Error);

        var v = resp.Data!;
        AnsiConsole.MarkupLine($"[green]removed[/] vault [bold]{v.Name.EscapeMarkup()}[/] (notes on disk untouched at {v.Path.EscapeMarkup()})");
        return 0;
    }

    public static async Task<int> ListAsync(IpcClient client, VaultScope scope)
    {
        var requestArgs = new Dictionary<string, string>();
        scope.ApplyTo(requestArgs);

        var resp = await client.SendAsync<List<NoteSummary>>("note:list", requestArgs);
        if (!resp.Success) return Error(resp.Error);

        var notes = resp.Data ?? [];
        if (notes.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]no notes[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Minimal);
        table.AddColumn("Name");
        table.AddColumn("Path");
        table.AddColumn(new TableColumn("Modified").RightAligned());
        foreach (var n in notes)
        {
            var when = DateTimeOffset.FromUnixTimeSeconds(n.Mtime).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
            table.AddRow(n.Name.EscapeMarkup(), n.Path.EscapeMarkup(), when);
        }
        AnsiConsole.Write(table);
        return 0;
    }

    public static async Task<int> ReadAsync(IpcClient client, string note, bool raw, VaultScope scope)
    {
        var requestArgs = new Dictionary<string, string> { ["path"] = VaultArgs.ResolvePath(scope, note) };
        scope.ApplyTo(requestArgs);

        var resp = await client.SendAsync<NoteContent>("note:read", requestArgs);
        if (!resp.Success) return Error(resp.Error);

        var content = resp.Data!;
        if (raw)
        {
            AnsiConsole.WriteLine(content.Raw);
            return 0;
        }

        if (content.Properties.Count > 0)
        {
            var table = new Table().Border(TableBorder.Minimal);
            table.AddColumn("Property");
            table.AddColumn("Value");
            foreach (var (k, v) in content.Properties)
                table.AddRow(k.EscapeMarkup(), v.EscapeMarkup());
            AnsiConsole.Write(table);
        }
        if (content.Tags.Count > 0)
            AnsiConsole.MarkupLine("[dim]tags:[/] " + string.Join(" ", content.Tags.Select(t => $"[blue]#{t.EscapeMarkup()}[/]")));
        AnsiConsole.WriteLine();
        MarkdownRenderer.Render(content.Body);
        return 0;
    }

    public static async Task<int> CreateAsync(IpcClient client, string note, string? content, VaultScope scope)
    {
        content ??= await ReadStdinOrError();
        if (content is null) return 64;

        var requestArgs = new Dictionary<string, string>
        {
            ["path"] = VaultArgs.ResolvePath(scope, note),
            ["content"] = content,
        };
        scope.ApplyTo(requestArgs);

        var resp = await client.SendAsync<NotePathResult>("note:create", requestArgs);
        if (!resp.Success) return Error(resp.Error);

        AnsiConsole.MarkupLine($"[green]created[/] {resp.Data!.Path.EscapeMarkup()}");
        return 0;
    }

    public static async Task<int> AppendAsync(IpcClient client, string note, string? content, bool inline, VaultScope scope)
    {
        content ??= await ReadStdinOrError();
        if (content is null) return 64;

        var requestArgs = new Dictionary<string, string>
        {
            ["path"] = VaultArgs.ResolvePath(scope, note),
            ["content"] = content,
        };
        scope.ApplyTo(requestArgs);
        if (inline) requestArgs["inline"] = "true";

        var resp = await client.SendAsync<NotePathResult>("note:append", requestArgs);
        if (!resp.Success) return Error(resp.Error);

        AnsiConsole.MarkupLine($"[green]appended[/] {resp.Data!.Path.EscapeMarkup()}");
        return 0;
    }

    public static async Task<int> DeleteAsync(IpcClient client, string note, VaultScope scope)
    {
        var requestArgs = new Dictionary<string, string> { ["path"] = VaultArgs.ResolvePath(scope, note) };
        scope.ApplyTo(requestArgs);

        var resp = await client.SendAsync<NotePathResult>("note:delete", requestArgs);
        if (!resp.Success) return Error(resp.Error);

        AnsiConsole.MarkupLine($"[green]deleted[/] {resp.Data!.Path.EscapeMarkup()}");
        return 0;
    }

    public static async Task<int> PropAsync(IpcClient client, string note, string? key, string? value, VaultScope scope)
    {
        var requestArgs = new Dictionary<string, string> { ["path"] = VaultArgs.ResolvePath(scope, note) };
        scope.ApplyTo(requestArgs);

        if (key is null)
        {
            var resp = await client.SendAsync<List<PropertyEntry>>("property:list", requestArgs);
            if (!resp.Success) return Error(resp.Error);
            var props = resp.Data ?? [];
            if (props.Count == 0) { AnsiConsole.MarkupLine("[dim]no properties[/]"); return 0; }
            var table = new Table().Border(TableBorder.Minimal);
            table.AddColumn("Property");
            table.AddColumn("Value");
            foreach (var p in props) table.AddRow(p.Key.EscapeMarkup(), p.Value.EscapeMarkup());
            AnsiConsole.Write(table);
            return 0;
        }

        requestArgs["key"] = key;

        if (value is null)
        {
            var resp = await client.SendAsync<string>("property:get", requestArgs);
            if (!resp.Success) return Error(resp.Error);
            AnsiConsole.WriteLine(resp.Data ?? "");
            return 0;
        }

        requestArgs["value"] = value;
        var setResp = await client.SendAsync<NotePathResult>("property:set", requestArgs);
        if (!setResp.Success) return Error(setResp.Error);
        AnsiConsole.MarkupLine($"[green]set[/] {key.EscapeMarkup()}={value.EscapeMarkup()} on {setResp.Data!.Path.EscapeMarkup()}");
        return 0;
    }

    public static async Task<int> DailyAsync(IpcClient client, string? date, VaultScope scope)
    {
        var requestArgs = new Dictionary<string, string>();
        scope.ApplyTo(requestArgs);
        if (date is not null) requestArgs["date"] = date;

        var resp = await client.SendAsync<NoteContent>("daily:read", requestArgs);
        if (!resp.Success) return Error(resp.Error);

        var note = resp.Data!;
        AnsiConsole.MarkupLine($"[dim]{note.Path.EscapeMarkup()}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine(string.IsNullOrWhiteSpace(note.Body) ? "(empty)" : note.Body);
        return 0;
    }

    public static async Task<int> DailyAppendAsync(IpcClient client, string? content, bool inline, string? date, VaultScope scope)
    {
        content ??= await ReadStdinOrError();
        if (content is null) return 64;

        var requestArgs = new Dictionary<string, string> { ["content"] = content };
        scope.ApplyTo(requestArgs);
        if (date is not null) requestArgs["date"] = date;
        if (inline) requestArgs["inline"] = "true";

        var resp = await client.SendAsync<NotePathResult>("daily:append", requestArgs);
        if (!resp.Success) return Error(resp.Error);

        AnsiConsole.MarkupLine($"[green]appended[/] {resp.Data!.Path.EscapeMarkup()}");
        return 0;
    }

    public static async Task<int> DaemonStatusAsync(IpcClient client)
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

    public static Task<int> DaemonInstallAsync(string? daemonPath) =>
        RunInstaller(daemonPath, i => i.InstallAsync());

    public static Task<int> DaemonUninstallAsync(string? daemonPath) =>
        RunInstaller(daemonPath, i => i.UninstallAsync());

    public static Task<int> DaemonRestartAsync(string? daemonPath) =>
        RunInstaller(daemonPath, i => i.RestartAsync());

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
            return Task.FromResult(Error(ex.Message + " (use --daemon-path to override)"));
        }
    }

    internal static IEnumerable<string> ExpandTags(IEnumerable<string> tags) =>
        tags.SelectMany(t => t.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    internal static IEnumerable<string> ExpandProperties(IEnumerable<string> items)
    {
        using var e = items.GetEnumerator();
        while (e.MoveNext())
        {
            var current = e.Current;
            if (current.Contains('='))
            {
                yield return current;
            }
            else if (e.MoveNext())
            {
                yield return $"{current}={e.Current}";
            }
            else
            {
                yield return current;
            }
        }
    }

    private static async Task<string?> ReadStdinOrError()
    {
        if (!Console.IsInputRedirected)
        {
            AnsiConsole.MarkupLine("[yellow]error:[/] no content. Use [bold]--content TEXT[/] or pipe stdin.");
            return null;
        }
        return await Console.In.ReadToEndAsync();
    }

    private static int Error(string? message)
    {
        AnsiConsole.MarkupLine($"[red]error:[/] {(message ?? "unknown error").EscapeMarkup()}");
        return 1;
    }
}
