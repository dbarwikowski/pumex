using System.CommandLine;
using Pumex.Contracts;
using Pumex.Ipc;
using Spectre.Console;

namespace Pumex.Cli.Commands;

internal static class PropCommand
{
    internal static Command Build()
    {
        var noteArg = new Argument<string>("note") { Description = "Note path or name" };
        var keyArg = new Argument<string?>("key")
        {
            Description = "Property key",
            DefaultValueFactory = _ => null,
            Arity = ArgumentArity.ZeroOrOne,
        };
        var valueArg = new Argument<string?>("value")
        {
            Description = "Property value (triggers set when provided)",
            DefaultValueFactory = _ => null,
            Arity = ArgumentArity.ZeroOrOne,
        };
        var vaultOpt = VaultOptions.Vault();
        var vaultPathOpt = VaultOptions.VaultPath();
        var cmd = new Command("prop", "List, get, or set note properties");
        cmd.Add(noteArg); cmd.Add(keyArg); cmd.Add(valueArg); cmd.Add(vaultOpt); cmd.Add(vaultPathOpt);
        cmd.SetAction(async r =>
        {
            var scope = VaultArgs.ScopeFrom(r.GetValue(vaultOpt), r.GetValue(vaultPathOpt), all: false);
            return await CommandHelpers.Run(c => RunAsync(c,
                r.GetValue(noteArg)!, r.GetValue(keyArg), r.GetValue(valueArg), scope));
        });
        return cmd;
    }

    private static async Task<int> RunAsync(IpcClient client, string note, string? key, string? value, VaultScope scope)
    {
        var requestArgs = new Dictionary<string, string> { ["path"] = VaultArgs.ResolvePath(scope, note) };
        scope.ApplyTo(requestArgs);

        if (key is null)
        {
            var resp = await client.SendAsync<List<PropertyEntry>>("property:list", requestArgs);
            if (!resp.Success) return CommandHelpers.Error(resp.Error);
            var props = resp.Data ?? [];
            if (props.Count == 0) { AnsiConsole.MarkupLine("[dim]no properties[/]"); return 0; }
            var table = new Table().Border(TableBorder.Minimal);
            table.AddColumn("Property");
            table.AddColumn("Value");
            foreach (var p in props) table.AddRow(p.Key.EscapeMarkup(), p.Value.EscapeMarkup());
            AnsiConsole.Write(table);
            return 0;
        }

        requestArgs["key"] = key;

        if (value is null)
        {
            var resp = await client.SendAsync<string>("property:get", requestArgs);
            if (!resp.Success) return CommandHelpers.Error(resp.Error);
            AnsiConsole.WriteLine(resp.Data ?? "");
            return 0;
        }

        requestArgs["value"] = value;
        var setResp = await client.SendAsync<NotePathResult>("property:set", requestArgs);
        if (!setResp.Success) return CommandHelpers.Error(setResp.Error);
        AnsiConsole.MarkupLine($"[green]set[/] {key.EscapeMarkup()}={value.EscapeMarkup()} on {setResp.Data!.Path.EscapeMarkup()}");
        return 0;
    }
}
