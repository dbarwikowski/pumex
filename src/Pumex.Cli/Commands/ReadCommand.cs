using System.CommandLine;
using Pumex.Contracts;
using Pumex.Ipc;
using Spectre.Console;

namespace Pumex.Cli.Commands;

internal static class ReadCommand
{
    internal static Command Build()
    {
        var noteArg = new Argument<string>("note") { Description = "Note path or name" };
        var rawOpt = new Option<bool>("--raw") { Description = "Print raw Markdown without rendering" };
        var vaultOpt = VaultOptions.Vault();
        var vaultPathOpt = VaultOptions.VaultPath();
        var cmd = new Command("read", "Read a note");
        cmd.Add(noteArg); cmd.Add(rawOpt); cmd.Add(vaultOpt); cmd.Add(vaultPathOpt);
        cmd.SetAction(async r =>
        {
            var scope = VaultArgs.ScopeFrom(r.GetValue(vaultOpt), r.GetValue(vaultPathOpt), all: false);
            return await CommandHelpers.Run(c => RunAsync(c, r.GetValue(noteArg)!, r.GetValue(rawOpt), scope));
        });
        return cmd;
    }

    private static async Task<int> RunAsync(IpcClient client, string note, bool raw, VaultScope scope)
    {
        var requestArgs = new Dictionary<string, string> { ["path"] = VaultArgs.ResolvePath(scope, note) };
        scope.ApplyTo(requestArgs);

        var resp = await client.SendAsync<NoteContent>("note:read", requestArgs);
        if (!resp.Success) return CommandHelpers.Error(resp.Error);

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
        DocumentRenderer.Render(content.Path, content.Body);
        return 0;
    }
}
