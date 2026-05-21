using System.CommandLine;
using Pumex.Cli;
using Pumex.Ipc;
using Spectre.Console;

var root = new RootCommand("pumex — headless markdown vault");

// ping
{
    var cmd = new Command("ping", "Check that the daemon is running");
    cmd.SetAction(async _ => await Run(Commands.PingAsync));
    root.Add(cmd);
}

// new
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
    cmd.SetAction(async r => await Run(c => Commands.NewVaultAsync(c,
        r.GetValue(nameArg)!,
        r.GetValue(pathArg))));
    root.Add(cmd);
}

// vault
{
    var vaultCmd = new Command("vault", "Manage registered vaults");

    {
        var cmd = new Command("list", "List registered vaults");
        cmd.SetAction(async _ => await Run(Commands.VaultListAsync));
        vaultCmd.Add(cmd);
    }
    {
        var nameArg = new Argument<string>("name") { Description = "Vault name" };
        var pathArg = new Argument<string>("path") { Description = "Vault directory" };
        var cmd = new Command("add", "Register a vault");
        cmd.Add(nameArg);
        cmd.Add(pathArg);
        cmd.SetAction(async r => await Run(c => Commands.VaultAddAsync(c,
            r.GetValue(nameArg)!, r.GetValue(pathArg)!)));
        vaultCmd.Add(cmd);
    }
    {
        var nameArg = new Argument<string>("name") { Description = "Vault name" };
        var cmd = new Command("remove", "Unregister a vault");
        cmd.Add(nameArg);
        cmd.SetAction(async r => await Run(c => Commands.VaultRemoveAsync(c, r.GetValue(nameArg)!)));
        vaultCmd.Add(cmd);
    }

    root.Add(vaultCmd);
}

// search
{
    var queryArg = new Argument<string?>("query")
    {
        Description = "Full-text search query",
        DefaultValueFactory = _ => null,
        Arity = ArgumentArity.ZeroOrOne,
    };
    var tagOpt = new Option<string[]>("--tag")
    {
        Description = "Filter by tag (repeatable; comma-separated values accepted)",
        Arity = ArgumentArity.ZeroOrMore,
    };
    var propOpt = new Option<string[]>("--property")
    {
        Description = "Filter by property: k=v or k v (repeatable)",
        Arity = ArgumentArity.ZeroOrMore,
        AllowMultipleArgumentsPerToken = true,
    };
    var limitOpt = new Option<int?>("--limit") { Description = "Maximum number of results" };
    var vaultOpt = new Option<string?>("--vault") { Description = "Named vault to use" };
    var vaultPathOpt = new Option<string?>("--vault-path") { Description = "Vault path to use" };
    var allOpt = new Option<bool>("--all") { Description = "Apply to all vaults" };

    var cmd = new Command("search", "Search notes by text, tag, or property");
    cmd.Add(queryArg);
    cmd.Add(tagOpt);
    cmd.Add(propOpt);
    cmd.Add(limitOpt);
    cmd.Add(vaultOpt);
    cmd.Add(vaultPathOpt);
    cmd.Add(allOpt);
    cmd.SetAction(async r =>
    {
        var scope = VaultArgs.ScopeFrom(r.GetValue(vaultOpt), r.GetValue(vaultPathOpt), r.GetValue(allOpt));
        return await Run(c => Commands.SearchAsync(c,
            r.GetValue(queryArg),
            r.GetValue(tagOpt) ?? [],
            r.GetValue(propOpt) ?? [],
            r.GetValue(limitOpt),
            scope));
    });
    root.Add(cmd);
}

// tags
{
    var vaultOpt = new Option<string?>("--vault") { Description = "Named vault to use" };
    var vaultPathOpt = new Option<string?>("--vault-path") { Description = "Vault path to use" };
    var allOpt = new Option<bool>("--all") { Description = "Apply to all vaults" };
    var cmd = new Command("tags", "List all tags with counts");
    cmd.Add(vaultOpt);
    cmd.Add(vaultPathOpt);
    cmd.Add(allOpt);
    cmd.SetAction(async r =>
    {
        var scope = VaultArgs.ScopeFrom(r.GetValue(vaultOpt), r.GetValue(vaultPathOpt), r.GetValue(allOpt));
        return await Run(c => Commands.TagsAsync(c, scope));
    });
    root.Add(cmd);
}

// backlinks
{
    var noteArg = new Argument<string>("note") { Description = "Note path or name" };
    var vaultOpt = new Option<string?>("--vault") { Description = "Named vault to use" };
    var vaultPathOpt = new Option<string?>("--vault-path") { Description = "Vault path to use" };
    var allOpt = new Option<bool>("--all") { Description = "Apply to all vaults" };
    var cmd = new Command("backlinks", "List notes that link to this note");
    cmd.Add(noteArg);
    cmd.Add(vaultOpt);
    cmd.Add(vaultPathOpt);
    cmd.Add(allOpt);
    cmd.SetAction(async r =>
    {
        var scope = VaultArgs.ScopeFrom(r.GetValue(vaultOpt), r.GetValue(vaultPathOpt), r.GetValue(allOpt));
        return await Run(c => Commands.BacklinksAsync(c, r.GetValue(noteArg)!, scope));
    });
    root.Add(cmd);
}

// list
{
    var vaultOpt = new Option<string?>("--vault") { Description = "Named vault to use" };
    var vaultPathOpt = new Option<string?>("--vault-path") { Description = "Vault path to use" };
    var allOpt = new Option<bool>("--all") { Description = "Apply to all vaults" };
    var cmd = new Command("list", "List notes in the vault");
    cmd.Add(vaultOpt);
    cmd.Add(vaultPathOpt);
    cmd.Add(allOpt);
    cmd.SetAction(async r =>
    {
        var scope = VaultArgs.ScopeFrom(r.GetValue(vaultOpt), r.GetValue(vaultPathOpt), r.GetValue(allOpt));
        return await Run(c => Commands.ListAsync(c, scope));
    });
    root.Add(cmd);
}

// read
{
    var noteArg = new Argument<string>("note") { Description = "Note path or name" };
    var rawOpt = new Option<bool>("--raw") { Description = "Print raw Markdown without rendering" };
    var vaultOpt = new Option<string?>("--vault") { Description = "Named vault to use" };
    var vaultPathOpt = new Option<string?>("--vault-path") { Description = "Vault path to use" };
    var cmd = new Command("read", "Read a note");
    cmd.Add(noteArg);
    cmd.Add(rawOpt);
    cmd.Add(vaultOpt);
    cmd.Add(vaultPathOpt);
    cmd.SetAction(async r =>
    {
        var scope = VaultArgs.ScopeFrom(r.GetValue(vaultOpt), r.GetValue(vaultPathOpt), all: false);
        return await Run(c => Commands.ReadAsync(c, r.GetValue(noteArg)!, r.GetValue(rawOpt), scope));
    });
    root.Add(cmd);
}

// create
{
    var noteArg = new Argument<string>("note") { Description = "Note path or name" };
    var contentOpt = new Option<string?>("--content") { Description = "Note content (omit to read from stdin)" };
    var vaultOpt = new Option<string?>("--vault") { Description = "Named vault to use" };
    var vaultPathOpt = new Option<string?>("--vault-path") { Description = "Vault path to use" };
    var cmd = new Command("create", "Create a note");
    cmd.Add(noteArg);
    cmd.Add(contentOpt);
    cmd.Add(vaultOpt);
    cmd.Add(vaultPathOpt);
    cmd.SetAction(async r =>
    {
        var scope = VaultArgs.ScopeFrom(r.GetValue(vaultOpt), r.GetValue(vaultPathOpt), all: false);
        return await Run(c => Commands.CreateAsync(c, r.GetValue(noteArg)!, r.GetValue(contentOpt), scope));
    });
    root.Add(cmd);
}

// append
{
    var noteArg = new Argument<string>("note") { Description = "Note path or name" };
    var contentOpt = new Option<string?>("--content") { Description = "Content to append (omit to read from stdin)" };
    var inlineOpt = new Option<bool>("--inline") { Description = "Append inline (no leading newline)" };
    var vaultOpt = new Option<string?>("--vault") { Description = "Named vault to use" };
    var vaultPathOpt = new Option<string?>("--vault-path") { Description = "Vault path to use" };
    var cmd = new Command("append", "Append to a note");
    cmd.Add(noteArg);
    cmd.Add(contentOpt);
    cmd.Add(inlineOpt);
    cmd.Add(vaultOpt);
    cmd.Add(vaultPathOpt);
    cmd.SetAction(async r =>
    {
        var scope = VaultArgs.ScopeFrom(r.GetValue(vaultOpt), r.GetValue(vaultPathOpt), all: false);
        return await Run(c => Commands.AppendAsync(c,
            r.GetValue(noteArg)!, r.GetValue(contentOpt), r.GetValue(inlineOpt), scope));
    });
    root.Add(cmd);
}

// delete
{
    var noteArg = new Argument<string>("note") { Description = "Note path or name" };
    var vaultOpt = new Option<string?>("--vault") { Description = "Named vault to use" };
    var vaultPathOpt = new Option<string?>("--vault-path") { Description = "Vault path to use" };
    var cmd = new Command("delete", "Delete a note");
    cmd.Add(noteArg);
    cmd.Add(vaultOpt);
    cmd.Add(vaultPathOpt);
    cmd.SetAction(async r =>
    {
        var scope = VaultArgs.ScopeFrom(r.GetValue(vaultOpt), r.GetValue(vaultPathOpt), all: false);
        return await Run(c => Commands.DeleteAsync(c, r.GetValue(noteArg)!, scope));
    });
    root.Add(cmd);
}

// prop
{
    var noteArg = new Argument<string>("note") { Description = "Note path or name" };
    var keyArg = new Argument<string?>("key")
    {
        Description = "Property key",
        DefaultValueFactory = _ => null,
        Arity = ArgumentArity.ZeroOrOne,
    };
    var valueArg = new Argument<string?>("value")
    {
        Description = "Property value (triggers set when provided)",
        DefaultValueFactory = _ => null,
        Arity = ArgumentArity.ZeroOrOne,
    };
    var vaultOpt = new Option<string?>("--vault") { Description = "Named vault to use" };
    var vaultPathOpt = new Option<string?>("--vault-path") { Description = "Vault path to use" };
    var cmd = new Command("prop", "List, get, or set note properties");
    cmd.Add(noteArg);
    cmd.Add(keyArg);
    cmd.Add(valueArg);
    cmd.Add(vaultOpt);
    cmd.Add(vaultPathOpt);
    cmd.SetAction(async r =>
    {
        var scope = VaultArgs.ScopeFrom(r.GetValue(vaultOpt), r.GetValue(vaultPathOpt), all: false);
        return await Run(c => Commands.PropAsync(c,
            r.GetValue(noteArg)!, r.GetValue(keyArg), r.GetValue(valueArg), scope));
    });
    root.Add(cmd);
}

// daily
{
    var dailyCmd = new Command("daily", "Read today's daily note");
    var dateOpt = new Option<string?>("--date") { Description = "Date in YYYY-MM-DD format (default: today)" };
    var vaultOpt = new Option<string?>("--vault") { Description = "Named vault to use" };
    var vaultPathOpt = new Option<string?>("--vault-path") { Description = "Vault path to use" };
    dailyCmd.Add(dateOpt);
    dailyCmd.Add(vaultOpt);
    dailyCmd.Add(vaultPathOpt);
    dailyCmd.SetAction(async r =>
    {
        var scope = VaultArgs.ScopeFrom(r.GetValue(vaultOpt), r.GetValue(vaultPathOpt), all: false);
        return await Run(c => Commands.DailyAsync(c, r.GetValue(dateOpt), scope));
    });

    {
        var contentOpt = new Option<string?>("--content") { Description = "Content to append (omit to read from stdin)" };
        var inlineOpt = new Option<bool>("--inline") { Description = "Append inline (no leading newline)" };
        var appendDateOpt = new Option<string?>("--date") { Description = "Date in YYYY-MM-DD format (default: today)" };
        var appendVaultOpt = new Option<string?>("--vault") { Description = "Named vault to use" };
        var appendVaultPathOpt = new Option<string?>("--vault-path") { Description = "Vault path to use" };
        var appendCmd = new Command("append", "Append to today's daily note");
        appendCmd.Add(contentOpt);
        appendCmd.Add(inlineOpt);
        appendCmd.Add(appendDateOpt);
        appendCmd.Add(appendVaultOpt);
        appendCmd.Add(appendVaultPathOpt);
        appendCmd.SetAction(async r =>
        {
            var scope = VaultArgs.ScopeFrom(r.GetValue(appendVaultOpt), r.GetValue(appendVaultPathOpt), all: false);
            return await Run(c => Commands.DailyAppendAsync(c,
                r.GetValue(contentOpt), r.GetValue(inlineOpt), r.GetValue(appendDateOpt), scope));
        });
        dailyCmd.Add(appendCmd);
    }

    root.Add(dailyCmd);
}

// daemon
{
    var daemonCmd = new Command("daemon", "Manage the pumex daemon");

    {
        var cmd = new Command("status", "Show daemon status");
        cmd.SetAction(async _ => await Run(Commands.DaemonStatusAsync));
        daemonCmd.Add(cmd);
    }
    {
        var daemonPathOpt = new Option<string?>("--daemon-path") { Description = "Path to the daemon binary" };
        var cmd = new Command("install", "Install the daemon as a system service");
        cmd.Add(daemonPathOpt);
        cmd.SetAction(async r => await Commands.DaemonInstallAsync(r.GetValue(daemonPathOpt)));
        daemonCmd.Add(cmd);
    }
    {
        var daemonPathOpt = new Option<string?>("--daemon-path") { Description = "Path to the daemon binary" };
        var cmd = new Command("uninstall", "Uninstall the daemon");
        cmd.Add(daemonPathOpt);
        cmd.SetAction(async r => await Commands.DaemonUninstallAsync(r.GetValue(daemonPathOpt)));
        daemonCmd.Add(cmd);
    }
    {
        var daemonPathOpt = new Option<string?>("--daemon-path") { Description = "Path to the daemon binary" };
        var cmd = new Command("start", "Start the daemon (spawn detached, idempotent)");
        cmd.Add(daemonPathOpt);
        cmd.SetAction(async r => await Commands.DaemonStartAsync(r.GetValue(daemonPathOpt)));
        daemonCmd.Add(cmd);
    }
    {
        var cmd = new Command("stop", "Stop the daemon gracefully via IPC");
        cmd.SetAction(async _ => await Commands.DaemonStopAsync());
        daemonCmd.Add(cmd);
    }
    {
        var daemonPathOpt = new Option<string?>("--daemon-path") { Description = "Path to the daemon binary" };
        var cmd = new Command("restart", "Restart the daemon (stop + start)");
        cmd.Add(daemonPathOpt);
        cmd.SetAction(async r => await Commands.DaemonRestartAsync(r.GetValue(daemonPathOpt)));
        daemonCmd.Add(cmd);
    }

    root.Add(daemonCmd);
}

return await root.Parse(args).InvokeAsync();

async Task<int> Run(Func<IpcClient, Task<int>> action)
{
    var client = new IpcClient();
    try
    {
        return await action(client);
    }
    catch (TimeoutException)
    {
        AnsiConsole.MarkupLine("[red]Could not connect to daemon[/]. Is it running? Try [yellow]pumex daemon status[/].");
        return 2;
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]error:[/] {ex.Message.EscapeMarkup()}");
        return 1;
    }
}
