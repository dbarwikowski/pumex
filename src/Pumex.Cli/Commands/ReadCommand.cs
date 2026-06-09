using System.CommandLine;
using Pumex.Contracts;
using Pumex.Ipc;

namespace Pumex.Cli.Commands;

internal static class ReadCommand
{
    internal static Command Build()
    {
        var noteArg = new Argument<string>("note") { Description = "Note path or name" };
        var rawOpt = new Option<bool>("--raw") { Description = "Print raw Markdown without rendering" };
        var limitOpt = new Option<int>("--limit") { Description = "Max rows to render for tabular formats (CSV/TSV)", DefaultValueFactory = _ => 100 };
        var tasksOpt = new Option<bool>("--tasks") { Description = "List the note's checkbox items instead of rendering it" };
        var pendingOpt = new Option<bool>("--pending") { Description = "With --tasks, show only unchecked items (numbering is unchanged)" };
        var vaultOpt = VaultOptions.Vault();
        var vaultPathOpt = VaultOptions.VaultPath();
        var cmd = new Command("read", "Read a note");
        cmd.Add(noteArg); cmd.Add(rawOpt); cmd.Add(limitOpt); cmd.Add(tasksOpt); cmd.Add(pendingOpt); cmd.Add(vaultOpt); cmd.Add(vaultPathOpt);
        cmd.SetAction(async r =>
        {
            var scope = VaultArgs.ScopeFrom(r.GetValue(vaultOpt), r.GetValue(vaultPathOpt), all: false);
            return await CommandHelpers.Run(c => RunAsync(c, r.GetValue(noteArg)!,
                r.GetValue(rawOpt), r.GetValue(limitOpt), r.GetValue(tasksOpt), r.GetValue(pendingOpt), scope));
        });
        return cmd;
    }

    private static async Task<int> RunAsync(IpcClient client, string note, bool raw, int limit, bool tasks, bool pending, VaultScope scope)
    {
        var requestArgs = new Dictionary<string, string> { ["path"] = VaultArgs.ResolvePath(scope, note) };
        scope.ApplyTo(requestArgs);

        if (tasks)
        {
            var taskResp = await client.SendAsync<List<CheckboxItem>>("note:tasks", requestArgs);
            if (!taskResp.Success) return CommandHelpers.Error(taskResp.Error);
            return NoteOutput.RenderCheckboxes(taskResp.Data ?? [], pending);
        }

        var resp = await client.SendAsync<NoteContent>("note:read", requestArgs);
        if (!resp.Success) return CommandHelpers.Error(resp.Error);

        NoteOutput.Render(resp.Data!, raw, limit);
        return 0;
    }
}
