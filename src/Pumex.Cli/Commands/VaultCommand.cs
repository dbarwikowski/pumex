using System.CommandLine;
using Pumex.Contracts;
using Pumex.Ipc;
using Spectre.Console;

namespace Pumex.Cli.Commands;

internal static class VaultCommand
{
    internal static Command Build()
    {
        var cmd = new Command("vault", "Manage registered vaults");
        cmd.Add(BuildList());
        cmd.Add(BuildAdd());
        cmd.Add(BuildRemove());
        return cmd;
    }

    private static Command BuildList()
    {
        var cmd = new Command("list", "List registered vaults");
        cmd.SetAction(async _ => await CommandHelpers.Run(ListAsync));
        return cmd;
    }

    private static Command BuildAdd()
    {
        var nameArg = new Argument<string>("name") { Description = "Vault name" };
        var pathArg = new Argument<string>("path") { Description = "Vault directory" };
        var cmd = new Command("add", "Register a vault");
        cmd.Add(nameArg);
        cmd.Add(pathArg);
        cmd.SetAction(async r => await CommandHelpers.Run(c => AddAsync(c, r.GetValue(nameArg)!, r.GetValue(pathArg)!)));
        return cmd;
    }

    private static Command BuildRemove()
    {
        var nameArg = new Argument<string>("name") { Description = "Vault name" };
        var cmd = new Command("remove", "Unregister a vault");
        cmd.Add(nameArg);
        cmd.SetAction(async r => await CommandHelpers.Run(c => RemoveAsync(c, r.GetValue(nameArg)!)));
        return cmd;
    }

    private static async Task<int> ListAsync(IpcClient client)
    {
        var resp = await client.SendAsync<List<VaultRecord>>("vaults");
        if (!resp.Success) return CommandHelpers.Error(resp.Error);

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

    private static async Task<int> AddAsync(IpcClient client, string name, string path)
    {
        var resp = await client.SendAsync<VaultRecord>("vault:add", new()
        {
            ["name"] = name,
            ["path"] = Path.GetFullPath(path),
        });
        if (!resp.Success) return CommandHelpers.Error(resp.Error);

        var v = resp.Data!;
        AnsiConsole.MarkupLine($"[green]added[/] vault [bold]{v.Name.EscapeMarkup()}[/] at {v.Path.EscapeMarkup()}");
        return 0;
    }

    private static async Task<int> RemoveAsync(IpcClient client, string name)
    {
        var resp = await client.SendAsync<VaultRecord>("vault:remove", new() { ["name"] = name });
        if (!resp.Success) return CommandHelpers.Error(resp.Error);

        var v = resp.Data!;
        AnsiConsole.MarkupLine($"[green]removed[/] vault [bold]{v.Name.EscapeMarkup()}[/] (notes on disk untouched at {v.Path.EscapeMarkup()})");
        return 0;
    }
}
