using Pumex.Cli;
using Pumex.Contracts;
using Spectre.Console;

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    PrintUsage();
    return 0;
}

var client = new IpcClient();

try
{
    return args[0] switch
    {
        "ping" => await Commands.PingAsync(client),
        "new" => await Commands.NewVaultAsync(client, args[1..]),
        "search" => await Commands.SearchAsync(client, args[1..]),
        "tags" => await Commands.TagsAsync(client, args[1..]),
        "backlinks" => await Commands.BacklinksAsync(client, args[1..]),
        "vaults" => await Commands.VaultsAsync(client),
        "vault" => await Commands.VaultAsync(client, args[1..]),
        "note" => await Commands.NoteAsync(client, args[1..]),
        "property" => await Commands.PropertyAsync(client, args[1..]),
        "daemon" => await Commands.DaemonAsync(client, args[1..]),
        _ => UnknownCommand(args[0]),
    };
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

static int UnknownCommand(string command)
{
    AnsiConsole.MarkupLine($"[red]Unknown command:[/] {command.EscapeMarkup()}");
    PrintUsage();
    return 64; // EX_USAGE
}

static void PrintUsage()
{
    AnsiConsole.WriteLine("pumex — headless markdown vault");
    AnsiConsole.WriteLine();
    AnsiConsole.WriteLine("Usage:");
    AnsiConsole.WriteLine("  pumex ping");
    AnsiConsole.WriteLine("  pumex new <name> [path]");
    AnsiConsole.WriteLine("  pumex search <query> [--limit N] [--vault NAME | --vault-path PATH | --all]");
    AnsiConsole.WriteLine("  pumex tags [--vault NAME | --vault-path PATH | --all]");
    AnsiConsole.WriteLine("  pumex backlinks <path-or-name> [--vault NAME | --vault-path PATH | --all]");
    AnsiConsole.WriteLine("  pumex vaults");
    AnsiConsole.WriteLine("  pumex vault add <name> <path>");
    AnsiConsole.WriteLine("  pumex note read <path-or-name> [--raw] [--vault NAME | --vault-path PATH]");
    AnsiConsole.WriteLine("  pumex note create <path-or-name> [--content TEXT | --stdin] [--vault NAME | --vault-path PATH]");
    AnsiConsole.WriteLine("  pumex note append <path-or-name> [--content TEXT | --stdin] [--inline] [--vault NAME | --vault-path PATH]");
    AnsiConsole.WriteLine("  pumex property list <path-or-name> [--vault NAME | --vault-path PATH]");
    AnsiConsole.WriteLine("  pumex property get  <path-or-name> <key> [--vault NAME | --vault-path PATH]");
    AnsiConsole.WriteLine("  pumex property set  <path-or-name> <key> <value> [--vault NAME | --vault-path PATH]");
    AnsiConsole.WriteLine("  pumex daemon <status|install|uninstall|restart> [--daemon-path PATH]");
    AnsiConsole.WriteLine();
    AnsiConsole.WriteLine("Commands that operate on vault contents default to the vault containing the");
    AnsiConsole.WriteLine("current directory. Use --vault, --vault-path, or --all to override.");
    AnsiConsole.WriteLine();
    AnsiConsole.WriteLine("A bare name (e.g. 'today') resolves to the matching note in the vault index.");
    AnsiConsole.WriteLine("Use a path with separators (e.g. 'sub/today.md') for path-based addressing.");
}
