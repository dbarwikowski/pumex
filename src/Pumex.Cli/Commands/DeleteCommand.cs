using System.CommandLine;
using Pumex.Contracts;
using Pumex.Ipc;
using Spectre.Console;

namespace Pumex.Cli.Commands;

internal static class DeleteCommand
{
    internal static Command Build()
    {
        var noteArg = new Argument<string>("note") { Description = "Note path or name" };
        var vaultOpt = VaultOptions.Vault();
        var vaultPathOpt = VaultOptions.VaultPath();
        var cmd = new Command("delete", "Delete a note");
        cmd.Add(noteArg); cmd.Add(vaultOpt); cmd.Add(vaultPathOpt);
        cmd.SetAction(async r =>
        {
            var scope = VaultArgs.ScopeFrom(r.GetValue(vaultOpt), r.GetValue(vaultPathOpt), all: false);
            return await CommandHelpers.Run(c => RunAsync(c, r.GetValue(noteArg)!, scope));
        });
        return cmd;
    }

    private static async Task<int> RunAsync(IpcClient client, string note, VaultScope scope)
    {
        var requestArgs = new Dictionary<string, string> { ["path"] = VaultArgs.ResolvePath(scope, note) };
        scope.ApplyTo(requestArgs);

        var resp = await client.SendAsync<NotePathResult>("note:delete", requestArgs);
        if (!resp.Success) return CommandHelpers.Error(resp.Error);

        AnsiConsole.MarkupLine($"[green]deleted[/] {resp.Data!.Path.EscapeMarkup()}");
        return 0;
    }
}
