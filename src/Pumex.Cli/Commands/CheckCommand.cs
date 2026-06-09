using System.CommandLine;
using Pumex.Contracts;
using Pumex.Ipc;
using Spectre.Console;

namespace Pumex.Cli.Commands;

internal static class CheckCommand
{
    internal static Command Build()
    {
        var noteArg = new Argument<string>("note") { Description = "Note path or name" };
        var indexArg = new Argument<int>("n") { Description = "Checkbox number (as shown by 'read --tasks')" };
        var vaultOpt = VaultOptions.Vault();
        var vaultPathOpt = VaultOptions.VaultPath();
        var cmd = new Command("check", "Toggle a checkbox in a note (run again to undo)");
        cmd.Add(noteArg); cmd.Add(indexArg); cmd.Add(vaultOpt); cmd.Add(vaultPathOpt);
        cmd.SetAction(async r =>
        {
            var scope = VaultArgs.ScopeFrom(r.GetValue(vaultOpt), r.GetValue(vaultPathOpt), all: false);
            return await CommandHelpers.Run(c => RunAsync(c, r.GetValue(noteArg)!, r.GetValue(indexArg), scope));
        });
        return cmd;
    }

    private static async Task<int> RunAsync(IpcClient client, string note, int index, VaultScope scope)
    {
        if (index < 1)
            return CommandHelpers.Error("Checkbox number must be >= 1 (see 'read --tasks').");

        var requestArgs = new Dictionary<string, string>
        {
            ["path"] = VaultArgs.ResolvePath(scope, note),
            ["index"] = index.ToString(),
        };
        scope.ApplyTo(requestArgs);

        var resp = await client.SendAsync<CheckboxToggleResult>("note:check", requestArgs);
        if (!resp.Success) return CommandHelpers.Error(resp.Error);

        var item = resp.Data!;
        var label = item.Checked ? "[green]checked[/]" : "[yellow]unchecked[/]";
        AnsiConsole.MarkupLine($"{label} [dim]#{item.Index}[/] {item.Text.EscapeMarkup()}");
        return 0;
    }
}
