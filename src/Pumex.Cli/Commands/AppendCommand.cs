using System.CommandLine;
using Pumex.Contracts;
using Pumex.Ipc;
using Spectre.Console;

namespace Pumex.Cli.Commands;

internal static class AppendCommand
{
    internal static Command Build()
    {
        var noteArg = new Argument<string>("note") { Description = "Note path or name" };
        var contentOpt = new Option<string?>("--content") { Description = "Content to append (omit to read from stdin)" };
        var inlineOpt = new Option<bool>("--inline") { Description = "Append inline (no leading newline)" };
        var vaultOpt = VaultOptions.Vault();
        var vaultPathOpt = VaultOptions.VaultPath();
        var cmd = new Command("append", "Append to a note");
        cmd.Add(noteArg); cmd.Add(contentOpt); cmd.Add(inlineOpt); cmd.Add(vaultOpt); cmd.Add(vaultPathOpt);
        cmd.SetAction(async r =>
        {
            var scope = VaultArgs.ScopeFrom(r.GetValue(vaultOpt), r.GetValue(vaultPathOpt), all: false);
            return await CommandHelpers.Run(c => RunAsync(c,
                r.GetValue(noteArg)!, r.GetValue(contentOpt), r.GetValue(inlineOpt), scope));
        });
        return cmd;
    }

    private static async Task<int> RunAsync(IpcClient client, string note, string? content, bool inline, VaultScope scope)
    {
        content ??= await CommandHelpers.ReadStdinOrError();
        if (content is null) return 64;

        var requestArgs = new Dictionary<string, string>
        {
            ["path"] = VaultArgs.ResolvePath(scope, note),
            ["content"] = content,
        };
        scope.ApplyTo(requestArgs);
        if (inline) requestArgs["inline"] = "true";

        var resp = await client.SendAsync<NotePathResult>("note:append", requestArgs);
        if (!resp.Success) return CommandHelpers.Error(resp.Error);

        AnsiConsole.MarkupLine($"[green]appended[/] {resp.Data!.Path.EscapeMarkup()}");
        return 0;
    }
}
