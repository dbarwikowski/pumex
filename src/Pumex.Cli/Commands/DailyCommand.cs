using System.CommandLine;
using Pumex.Contracts;
using Pumex.Ipc;
using Spectre.Console;

namespace Pumex.Cli.Commands;

internal static class DailyCommand
{
    internal static Command Build()
    {
        var dateOpt = new Option<string?>("--date") { Description = "Date in YYYY-MM-DD format (default: today)" };
        var vaultOpt = VaultOptions.Vault();
        var vaultPathOpt = VaultOptions.VaultPath();
        var cmd = new Command("daily", "Read today's daily note");
        cmd.Add(dateOpt); cmd.Add(vaultOpt); cmd.Add(vaultPathOpt);
        cmd.SetAction(async r =>
        {
            var scope = VaultArgs.ScopeFrom(r.GetValue(vaultOpt), r.GetValue(vaultPathOpt), all: false);
            return await CommandHelpers.Run(c => ReadAsync(c, r.GetValue(dateOpt), scope));
        });

        cmd.Add(BuildAppend());
        return cmd;
    }

    private static Command BuildAppend()
    {
        var contentOpt = new Option<string?>("--content") { Description = "Content to append (omit to read from stdin)" };
        var inlineOpt = new Option<bool>("--inline") { Description = "Append inline (no leading newline)" };
        var dateOpt = new Option<string?>("--date") { Description = "Date in YYYY-MM-DD format (default: today)" };
        var vaultOpt = VaultOptions.Vault();
        var vaultPathOpt = VaultOptions.VaultPath();
        var cmd = new Command("append", "Append to today's daily note");
        cmd.Add(contentOpt); cmd.Add(inlineOpt); cmd.Add(dateOpt); cmd.Add(vaultOpt); cmd.Add(vaultPathOpt);
        cmd.SetAction(async r =>
        {
            var scope = VaultArgs.ScopeFrom(r.GetValue(vaultOpt), r.GetValue(vaultPathOpt), all: false);
            return await CommandHelpers.Run(c => AppendAsync(c,
                r.GetValue(contentOpt), r.GetValue(inlineOpt), r.GetValue(dateOpt), scope));
        });
        return cmd;
    }

    private static async Task<int> ReadAsync(IpcClient client, string? date, VaultScope scope)
    {
        var requestArgs = new Dictionary<string, string>();
        scope.ApplyTo(requestArgs);
        if (date is not null) requestArgs["date"] = date;

        var resp = await client.SendAsync<NoteContent>("daily:read", requestArgs);
        if (!resp.Success) return CommandHelpers.Error(resp.Error);

        var note = resp.Data!;
        AnsiConsole.MarkupLine($"[dim]{note.Path.EscapeMarkup()}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine(string.IsNullOrWhiteSpace(note.Body) ? "(empty)" : note.Body);
        return 0;
    }

    private static async Task<int> AppendAsync(IpcClient client, string? content, bool inline, string? date, VaultScope scope)
    {
        content ??= await CommandHelpers.ReadStdinOrError();
        if (content is null) return 64;

        var requestArgs = new Dictionary<string, string> { ["content"] = content };
        scope.ApplyTo(requestArgs);
        if (date is not null) requestArgs["date"] = date;
        if (inline) requestArgs["inline"] = "true";

        var resp = await client.SendAsync<NotePathResult>("daily:append", requestArgs);
        if (!resp.Success) return CommandHelpers.Error(resp.Error);

        AnsiConsole.MarkupLine($"[green]appended[/] {resp.Data!.Path.EscapeMarkup()}");
        return 0;
    }
}
