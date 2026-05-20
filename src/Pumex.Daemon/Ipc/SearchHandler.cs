using Pumex.Contracts;

namespace Pumex.Daemon.Ipc;

public class SearchHandler : ICommandHandler
{
    private readonly IndexDb _db;

    public string Command => "search";

    public SearchHandler(IndexDb db) => _db = db;

    public async Task<object?> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        // query is optional — `pumex search --tag foo` (no query) lists all
        // notes tagged "foo". One of (query, tags, properties) should be set;
        // we don't enforce that — an empty filter set returns the most-recently-
        // modified `limit` notes in the vault.
        var query = EscapeForFts(request.Optional("query"));
        var limit = request.Args.TryGetValue("limit", out var l) && int.TryParse(l, out var n) ? n : 50;
        var vault = await request.ResolveVaultAsync(_db);

        // Wire format: tags=tag1,tag2 ; properties=k1=v1;k2=v2 (semicolon delimited so values may contain `=`).
        var tags = request.Optional("tags") is { Length: > 0 } tagStr
            ? tagStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : null;

        List<KeyValuePair<string, string>>? properties = null;
        if (request.Optional("properties") is { Length: > 0 } propStr)
        {
            properties = new List<KeyValuePair<string, string>>();
            foreach (var pair in propStr.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq <= 0)
                    throw new ArgumentException($"property filter '{pair}' must be key=value");
                properties.Add(new(pair[..eq].Trim(), pair[(eq + 1)..].Trim()));
            }
        }

        return await _db.SearchAsync(query, limit, vault?.Id, tags, properties);
    }

    // FTS5's query parser treats `-`, `:`, `(`, `)`, and uppercase AND/OR/NOT/NEAR
    // as syntax. A user typing `smoke-test` would otherwise hit "no such column".
    // Pass through queries that already look structured; otherwise wrap each
    // whitespace-separated token as a phrase. Multiple phrases are AND-ed by
    // FTS5 — same semantics as the previous bareword default.
    internal static string? EscapeForFts(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return query;
        if (LooksStructured(query)) return query;
        var tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(' ', tokens.Select(t => "\"" + t.Replace("\"", "\"\"") + "\""));
    }

    private static bool LooksStructured(string q)
    {
        if (q.Contains('"') || q.Contains('(') || q.Contains(')')) return true;
        foreach (var kw in new[] { " AND ", " OR ", " NOT ", " NEAR(", "NEAR(" })
            if (q.Contains(kw, StringComparison.Ordinal)) return true;
        return false;
    }
}
