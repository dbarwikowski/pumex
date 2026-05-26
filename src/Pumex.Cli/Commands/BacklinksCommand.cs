using System.CommandLine;
using Pumex.Ipc;
using Spectre.Console;

namespace Pumex.Cli.Commands;

internal static class BacklinksCommand
{
    internal static Command Build()
    {
        var noteArg = new Argument<string>("note") { Description = "Note path or name" };
        var vaultOpt = VaultOptions.Vault();
        var vaultPathOpt = VaultOptions.VaultPath();
        var allOpt = VaultOptions.All();
        var cmd = new Command("backlinks", "List notes that link to this note");
        cmd.Add(noteArg); cmd.Add(vaultOpt); cmd.Add(vaultPathOpt); cmd.Add(allOpt);
        cmd.SetAction(async r =>
        {
            var scope = VaultArgs.ScopeFrom(r.GetValue(vaultOpt), r.GetValue(vaultPathOpt), r.GetValue(allOpt));
            return await CommandHelpers.Run(c => RunAsync(c, r.GetValue(noteArg)!, scope));
        });
        return cmd;
    }

    private static async Task<int> RunAsync(IpcClient client, string note, VaultScope scope)
    {
        var requestArgs = new Dictionary<string, string> { ["path"] = VaultArgs.ResolvePath(scope, note) };
        scope.ApplyTo(requestArgs);

        var resp = await client.SendAsync<List<string>>("backlinks", requestArgs);
        if (!resp.Success) return CommandHelpers.Error(resp.Error);

        var paths = resp.Data ?? [];
        if (paths.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]no backlinks[/]");
            return 0;
        }

        foreach (var p in paths) AnsiConsole.WriteLine(p);
        return 0;
    }
}
