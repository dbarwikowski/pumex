using System.CommandLine;
using System.Text;
using Pumex.Contracts;
using Pumex.Ipc;
using Spectre.Console;

namespace Pumex.Cli.Commands;

internal static class ContextCommand
{
    internal static Command Build()
    {
        var queryArg = new Argument<string>("query") { Description = "Text to gather context for" };
        var limitOpt = new Option<int?>("--limit") { Description = "Maximum number of sources (default 5)" };
        var budgetOpt = new Option<int?>("--budget") { Description = "Approx. passage-character budget (default 6000)" };
        var vaultOpt = VaultOptions.Vault();
        var vaultPathOpt = VaultOptions.VaultPath();
        var allOpt = VaultOptions.All();

        var cmd = new Command("context", "Gather agent-ready context for a query: ranked passages + read pointers");
        cmd.Add(queryArg); cmd.Add(limitOpt); cmd.Add(budgetOpt);
        cmd.Add(vaultOpt); cmd.Add(vaultPathOpt); cmd.Add(allOpt);
        cmd.SetAction(async r =>
        {
            var scope = VaultArgs.ScopeFrom(r.GetValue(vaultOpt), r.GetValue(vaultPathOpt), r.GetValue(allOpt));
            return await CommandHelpers.Run(c => RunAsync(c,
                r.GetValue(queryArg)!,
                r.GetValue(limitOpt),
                r.GetValue(budgetOpt),
                scope));
        });
        return cmd;
    }

    private static async Task<int> RunAsync(IpcClient client, string query, int? limit, int? budget, VaultScope scope)
    {
        var requestArgs = new Dictionary<string, string> { ["query"] = query };
        if (limit is not null) requestArgs["limit"] = limit.Value.ToString();
        if (budget is not null) requestArgs["budget"] = budget.Value.ToString();
        scope.ApplyTo(requestArgs);

        var resp = await client.SendAsync<List<ContextResult>>("context", requestArgs);
        if (!resp.Success) return CommandHelpers.Error(resp.Error);

        var results = resp.Data ?? [];
        if (results.Count == 0)
        {
            AnsiConsole.WriteLine($"No matches for \"{query}\"");
            return 0;
        }

        // Plain Markdown, written literally (no Spectre markup parsing) so the
        // output is a clean context pack an agent can paste verbatim.
        AnsiConsole.WriteLine(RenderMarkdown(query, results));
        return 0;
    }

    /// <summary>
    /// Renders the context pack: a header, then one block per source with a
    /// path heading + normalised score, the passage, and a <c>pumex read</c>
    /// pointer. Internal for unit testing.
    /// </summary>
    internal static string RenderMarkdown(string query, IReadOnlyList<ContextResult> results)
    {
        var sb = new StringBuilder();
        sb.Append("# Context: ").Append(query).Append('\n');
        sb.Append(results.Count).Append(results.Count == 1 ? " source" : " sources").Append(" · lexical\n");

        foreach (var r in results)
        {
            sb.Append('\n');
            sb.Append("## ").Append(r.RelativePath)
              .Append("  (score ").Append(r.Score.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append(")\n");
            sb.Append(r.Passage).Append('\n');
            sb.Append("→ pumex read ").Append(r.Pointer).Append('\n');
        }

        return sb.ToString().TrimEnd('\n');
    }
}
