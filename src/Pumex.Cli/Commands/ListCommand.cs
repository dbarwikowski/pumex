using System.CommandLine;
using Pumex.Contracts;
using Pumex.Ipc;
using Spectre.Console;

namespace Pumex.Cli.Commands;

internal static class ListCommand
{
    internal static Command Build()
    {
        var formatOpt = new Option<string[]>("--format", "--ext")
        {
            Description = "Filter by file format/extension, e.g. md, csv (repeatable; comma-separated accepted)",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var vaultOpt = VaultOptions.Vault();
        var vaultPathOpt = VaultOptions.VaultPath();
        var allOpt = VaultOptions.All();
        var cmd = new Command("list", "List notes in the vault");
        cmd.Add(formatOpt); cmd.Add(vaultOpt); cmd.Add(vaultPathOpt); cmd.Add(allOpt);
        cmd.SetAction(async r =>
        {
            var scope = VaultArgs.ScopeFrom(r.GetValue(vaultOpt), r.GetValue(vaultPathOpt), r.GetValue(allOpt));
            return await CommandHelpers.Run(c => RunAsync(c, r.GetValue(formatOpt) ?? [], scope));
        });
        return cmd;
    }

    private static async Task<int> RunAsync(IpcClient client, string[] formats, VaultScope scope)
    {
        var requestArgs = new Dictionary<string, string>();
        var expandedFormats = formats
            .SelectMany(f => f.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();
        if (expandedFormats.Count > 0) requestArgs["format"] = string.Join(',', expandedFormats);
        scope.ApplyTo(requestArgs);

        var resp = await client.SendAsync<List<NoteSummary>>("note:list", requestArgs);
        if (!resp.Success) return CommandHelpers.Error(resp.Error);

        var notes = resp.Data ?? [];
        if (notes.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]no notes[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Minimal);
        table.AddColumn("Name");
        table.AddColumn("Format");
        table.AddColumn("Path");
        table.AddColumn(new TableColumn("Modified").RightAligned());
        foreach (var n in notes)
        {
            var when = DateTimeOffset.FromUnixTimeSeconds(n.Mtime).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
            table.AddRow(n.Name.EscapeMarkup(), (n.Format ?? "").EscapeMarkup(), n.Path.EscapeMarkup(), when);
        }
        AnsiConsole.Write(table);
        return 0;
    }
}
