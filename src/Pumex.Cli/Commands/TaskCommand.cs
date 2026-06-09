using System.CommandLine;
using Pumex.Contracts;
using Pumex.Ipc;
using Spectre.Console;

namespace Pumex.Cli.Commands;

/// <summary>
/// Task notes under <c>&lt;vault&gt;/tasks/</c>. Verb-first subcommands:
/// <c>create</c>, <c>read</c>, <c>list</c>, <c>status</c>, <c>attach</c>.
/// </summary>
internal static class TaskCommand
{
    internal static Command Build()
    {
        var cmd = new Command("task", "Work with task notes (tasks/ folder)");
        cmd.Add(BuildCreate());
        cmd.Add(BuildRead());
        cmd.Add(BuildList());
        cmd.Add(BuildStatus());
        cmd.Add(BuildAttach());
        return cmd;
    }

    private static Command BuildCreate()
    {
        var nameArg = new Argument<string>("name") { Description = "Task name" };
        var contentOpt = new Option<string?>("--content") { Description = "Task note body" };
        var vaultOpt = VaultOptions.Vault();
        var vaultPathOpt = VaultOptions.VaultPath();
        var cmd = new Command("create", "Create a new task note");
        cmd.Add(nameArg); cmd.Add(contentOpt); cmd.Add(vaultOpt); cmd.Add(vaultPathOpt);
        cmd.SetAction(async r =>
        {
            var scope = VaultArgs.ScopeFrom(r.GetValue(vaultOpt), r.GetValue(vaultPathOpt), all: false);
            return await CommandHelpers.Run(async c =>
            {
                var args = new Dictionary<string, string> { ["name"] = r.GetValue(nameArg)! };
                if (r.GetValue(contentOpt) is { } content) args["content"] = content;
                scope.ApplyTo(args);

                var resp = await c.SendAsync<TaskResult>("task:create", args);
                if (!resp.Success) return CommandHelpers.Error(resp.Error);
                AnsiConsole.MarkupLine($"[green]created[/] {resp.Data!.Path.EscapeMarkup()}");
                return 0;
            });
        });
        return cmd;
    }

    private static Command BuildRead()
    {
        var nameArg = new Argument<string>("name") { Description = "Task name or path" };
        var rawOpt = new Option<bool>("--raw") { Description = "Print raw Markdown without rendering" };
        var limitOpt = new Option<int>("--limit") { Description = "Max rows to render for tabular bodies", DefaultValueFactory = _ => 100 };
        var vaultOpt = VaultOptions.Vault();
        var vaultPathOpt = VaultOptions.VaultPath();
        var cmd = new Command("read", "Read a task note");
        cmd.Add(nameArg); cmd.Add(rawOpt); cmd.Add(limitOpt); cmd.Add(vaultOpt); cmd.Add(vaultPathOpt);
        cmd.SetAction(async r =>
        {
            var scope = VaultArgs.ScopeFrom(r.GetValue(vaultOpt), r.GetValue(vaultPathOpt), all: false);
            return await CommandHelpers.Run(async c =>
            {
                var args = new Dictionary<string, string> { ["name"] = r.GetValue(nameArg)! };
                scope.ApplyTo(args);
                var resp = await c.SendAsync<NoteContent>("task:read", args);
                if (!resp.Success) return CommandHelpers.Error(resp.Error);
                NoteOutput.Render(resp.Data!, r.GetValue(rawOpt), r.GetValue(limitOpt));
                return 0;
            });
        });
        return cmd;
    }

    private static Command BuildList()
    {
        var statusOpt = new Option<string[]>("--status")
        {
            Description = "Filter by status (repeatable; comma-separated accepted)",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var openOpt = new Option<bool>("--open") { Description = "Show only tasks whose status is not DONE" };
        var vaultOpt = VaultOptions.Vault();
        var vaultPathOpt = VaultOptions.VaultPath();
        var cmd = new Command("list", "List task notes (newest first)");
        cmd.Add(statusOpt); cmd.Add(openOpt); cmd.Add(vaultOpt); cmd.Add(vaultPathOpt);
        cmd.SetAction(async r =>
        {
            var scope = VaultArgs.ScopeFrom(r.GetValue(vaultOpt), r.GetValue(vaultPathOpt), all: false);
            return await CommandHelpers.Run(c => ListAsync(c, r.GetValue(statusOpt) ?? [], r.GetValue(openOpt), scope));
        });
        return cmd;
    }

    private static async Task<int> ListAsync(IpcClient client, string[] statuses, bool open, VaultScope scope)
    {
        var expanded = statuses
            .SelectMany(s => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();

        var args = new Dictionary<string, string>();
        if (expanded.Count > 0) args["status"] = string.Join(',', expanded);
        if (open) args["open"] = "true";
        scope.ApplyTo(args);

        var resp = await client.SendAsync<List<TaskSummary>>("task:list", args);
        if (!resp.Success) return CommandHelpers.Error(resp.Error);

        var tasks = resp.Data ?? [];
        if (tasks.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]no tasks[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Minimal);
        table.AddColumn("Name");
        table.AddColumn("Status");
        table.AddColumn("Created");
        table.AddColumn("Completed");
        foreach (var t in tasks)
            table.AddRow(t.Name.EscapeMarkup(), t.Status.EscapeMarkup(), t.Created.EscapeMarkup(), t.Completed.EscapeMarkup());
        AnsiConsole.Write(table);
        return 0;
    }

    private static Command BuildStatus()
    {
        var nameArg = new Argument<string>("name") { Description = "Task name or path" };
        var valueArg = new Argument<string?>("status")
        {
            Description = "New status (omit to read the current one)",
            DefaultValueFactory = _ => null,
            Arity = ArgumentArity.ZeroOrOne,
        };
        var vaultOpt = VaultOptions.Vault();
        var vaultPathOpt = VaultOptions.VaultPath();
        var cmd = new Command("status", "Get or set a task's status");
        cmd.Add(nameArg); cmd.Add(valueArg); cmd.Add(vaultOpt); cmd.Add(vaultPathOpt);
        cmd.SetAction(async r =>
        {
            var scope = VaultArgs.ScopeFrom(r.GetValue(vaultOpt), r.GetValue(vaultPathOpt), all: false);
            return await CommandHelpers.Run(c => StatusAsync(c, r.GetValue(nameArg)!, r.GetValue(valueArg), scope));
        });
        return cmd;
    }

    private static async Task<int> StatusAsync(IpcClient client, string name, string? value, VaultScope scope)
    {
        var args = new Dictionary<string, string> { ["name"] = name };
        scope.ApplyTo(args);

        if (value is null)
        {
            var resp = await client.SendAsync<string>("task:status", args);
            if (!resp.Success) return CommandHelpers.Error(resp.Error);
            AnsiConsole.WriteLine(resp.Data ?? "");
            return 0;
        }

        args["value"] = value;
        var setResp = await client.SendAsync<TaskSummary>("task:status", args);
        if (!setResp.Success) return CommandHelpers.Error(setResp.Error);

        var t = setResp.Data!;
        var completed = string.IsNullOrEmpty(t.Completed) ? "" : $" [dim](completed {t.Completed})[/]";
        AnsiConsole.MarkupLine($"[green]{t.Status.EscapeMarkup()}[/] {t.Name.EscapeMarkup()}{completed}");
        return 0;
    }

    private static Command BuildAttach()
    {
        var nameArg = new Argument<string>("name") { Description = "Task name or path" };
        var fileArg = new Argument<string>("file") { Description = "File to move into the task folder" };
        var vaultOpt = VaultOptions.Vault();
        var vaultPathOpt = VaultOptions.VaultPath();
        var cmd = new Command("attach", "Move a file into a task's folder and link it");
        cmd.Add(nameArg); cmd.Add(fileArg); cmd.Add(vaultOpt); cmd.Add(vaultPathOpt);
        cmd.SetAction(async r =>
        {
            var scope = VaultArgs.ScopeFrom(r.GetValue(vaultOpt), r.GetValue(vaultPathOpt), all: false);
            return await CommandHelpers.Run(async c =>
            {
                // Resolve the source against the CLI's CWD — the daemon runs elsewhere.
                var args = new Dictionary<string, string>
                {
                    ["name"] = r.GetValue(nameArg)!,
                    ["file"] = Path.GetFullPath(r.GetValue(fileArg)!),
                };
                scope.ApplyTo(args);

                var resp = await c.SendAsync<TaskAttachResult>("task:attach", args);
                if (!resp.Success) return CommandHelpers.Error(resp.Error);
                AnsiConsole.MarkupLine($"[green]attached[/] {resp.Data!.AttachmentPath.EscapeMarkup()}");
                return 0;
            });
        });
        return cmd;
    }
}
