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
        var query = request.Optional("query");
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
}
