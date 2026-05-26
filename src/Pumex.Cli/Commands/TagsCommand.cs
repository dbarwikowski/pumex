using System.CommandLine;
using Pumex.Contracts;
using Pumex.Ipc;
using Spectre.Console;

namespace Pumex.Cli.Commands;

internal static class TagsCommand
{
    internal static Command Build()
    {
        var vaultOpt = VaultOptions.Vault();
        var vaultPathOpt = VaultOptions.VaultPath();
        var allOpt = VaultOptions.All();
        var cmd = new Command("tags", "List all tags with counts");
        cmd.Add(vaultOpt); cmd.Add(vaultPathOpt); cmd.Add(allOpt);
        cmd.SetAction(async r =>
        {
            var scope = VaultArgs.ScopeFrom(r.GetValue(vaultOpt), r.GetValue(vaultPathOpt), r.GetValue(allOpt));
            return await CommandHelpers.Run(c => RunAsync(c, scope));
        });
        return cmd;
    }

    private static async Task<int> RunAsync(IpcClient client, VaultScope scope)
    {
        var requestArgs = new Dictionary<string, string>();
        scope.ApplyTo(requestArgs);

        var resp = await client.SendAsync<List<TagCount>>("tags", requestArgs);
        if (!resp.Success) return CommandHelpers.Error(resp.Error);

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
}
