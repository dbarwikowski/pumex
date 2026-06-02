using System.CommandLine;
using Pumex.Contracts;
using Pumex.Ipc;
using Spectre.Console;

namespace Pumex.Cli.Commands;

internal static class SearchCommand
{
    internal static Command Build()
    {
        var queryArg = new Argument<string?>("query")
        {
            Description = "Full-text search query",
            DefaultValueFactory = _ => null,
            Arity = ArgumentArity.ZeroOrOne,
        };
        var tagOpt = new Option<string[]>("--tag")
        {
            Description = "Filter by tag (repeatable; comma-separated values accepted)",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var propOpt = new Option<string[]>("--property")
        {
            Description = "Filter by property: k=v or k v (repeatable)",
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = true,
        };
        var limitOpt = new Option<int?>("--limit") { Description = "Maximum number of results" };
        var formatOpt = new Option<string[]>("--format", "--ext")
        {
            Description = "Filter by file format/extension, e.g. md, csv (repeatable; comma-separated accepted)",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var vaultOpt = VaultOptions.Vault();
        var vaultPathOpt = VaultOptions.VaultPath();
        var allOpt = VaultOptions.All();

        var cmd = new Command("search", "Search notes by text, tag, or property");
        cmd.Add(queryArg); cmd.Add(tagOpt); cmd.Add(propOpt); cmd.Add(limitOpt); cmd.Add(formatOpt);
        cmd.Add(vaultOpt); cmd.Add(vaultPathOpt); cmd.Add(allOpt);
        cmd.SetAction(async r =>
        {
            var scope = VaultArgs.ScopeFrom(r.GetValue(vaultOpt), r.GetValue(vaultPathOpt), r.GetValue(allOpt));
            return await CommandHelpers.Run(c => RunAsync(c,
                r.GetValue(queryArg),
                r.GetValue(tagOpt) ?? [],
                r.GetValue(propOpt) ?? [],
                r.GetValue(limitOpt),
                r.GetValue(formatOpt) ?? [],
                scope));
        });
        return cmd;
    }

    private static async Task<int> RunAsync(
        IpcClient client, string? query, string[] tags, string[] properties, int? limit, string[] formats, VaultScope scope)
    {
        var expandedTags = CommandHelpers.ExpandTags(tags).ToList();
        var expandedProps = CommandHelpers.ExpandProperties(properties).ToList();
        var expandedFormats = formats
            .SelectMany(f => f.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();

        if (query is null && expandedTags.Count == 0 && expandedProps.Count == 0 && expandedFormats.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]usage:[/] pumex search <query> [[--tag X]] [[--property k=v]] [[--format csv]] [[--limit N]] [[--vault ...]]");
            return 64;
        }

        var requestArgs = new Dictionary<string, string>();
        if (query is not null) requestArgs["query"] = query;
        if (limit is not null) requestArgs["limit"] = limit.Value.ToString();
        if (expandedTags.Count > 0) requestArgs["tags"] = string.Join(',', expandedTags);
        if (expandedProps.Count > 0) requestArgs["properties"] = string.Join(';', expandedProps);
        if (expandedFormats.Count > 0) requestArgs["format"] = string.Join(',', expandedFormats);
        scope.ApplyTo(requestArgs);

        var resp = await client.SendAsync<List<SearchResult>>("search", requestArgs);
        if (!resp.Success) return CommandHelpers.Error(resp.Error);

        var results = resp.Data ?? [];
        if (results.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]no matches[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Minimal);
        table.AddColumn("Note");
        table.AddColumn("Format");
        table.AddColumn("Path");
        table.AddColumn("Snippet");
        foreach (var r in results)
            table.AddRow(r.Name.EscapeMarkup(), (r.Format ?? "").EscapeMarkup(), r.Path.EscapeMarkup(), r.Snippet.EscapeMarkup());
        AnsiConsole.Write(table);
        return 0;
    }
}
