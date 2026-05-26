using Pumex.Contracts;

namespace Pumex.Daemon;

public class SearchRepository(IndexDbContext context) : ISearchRepository
{
    public async Task<List<SearchResult>> SearchAsync(
        string? query,
        int limit = 50,
        long? vaultId = null,
        IReadOnlyList<string>? tags = null,
        IReadOnlyList<KeyValuePair<string, string>>? properties = null)
    {
        // Build SQL incrementally: FTS join only when query is non-empty,
        // optional vault scope, AND-semantics tag and property filters.
        // Snippet builder gets the original query (or null) and falls back to
        // the first non-empty body line when there's nothing to substring-match.
        //
        // Gate is released before BuildSnippet so synchronous disk reads do not
        // block other repository operations behind the single-connection semaphore.
        var hasQuery = !string.IsNullOrWhiteSpace(query);
        var sql = new System.Text.StringBuilder();
        var parameters = new List<(string, object)>();

        if (hasQuery)
        {
            sql.Append("""
                SELECT n.path, n.name
                FROM notes_fts
                JOIN notes n ON n.id = notes_fts.rowid
                WHERE notes_fts MATCH @query
                """);
            parameters.Add(("@query", query!));
        }
        else
        {
            sql.Append("""
                SELECT n.path, n.name
                FROM notes n
                WHERE 1=1
                """);
        }

        if (vaultId is not null)
        {
            sql.Append(" AND n.vault_id = @vaultId");
            parameters.Add(("@vaultId", vaultId.Value));
        }

        if (tags is not null)
        {
            for (var i = 0; i < tags.Count; i++)
            {
                var p = $"@tag_{i}";
                sql.Append($" AND EXISTS (SELECT 1 FROM tags WHERE note_id = n.id AND tag = {p})");
                parameters.Add((p, tags[i]));
            }
        }

        if (properties is not null)
        {
            for (var i = 0; i < properties.Count; i++)
            {
                var pk = $"@pk_{i}";
                var pv = $"@pv_{i}";
                sql.Append($" AND EXISTS (SELECT 1 FROM properties WHERE note_id = n.id AND key = {pk} AND value = {pv})");
                parameters.Add((pk, properties[i].Key));
                parameters.Add((pv, properties[i].Value));
            }
        }

        sql.Append(hasQuery ? " ORDER BY rank" : " ORDER BY n.mtime DESC");
        sql.Append(" LIMIT @limit");
        parameters.Add(("@limit", limit));

        List<(string Path, string Name)> matches;
        {
            using var _ = await context.AcquireAsync();
            matches = new List<(string Path, string Name)>();
            using var cmd = context.Command(sql.ToString(), parameters.ToArray());
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                matches.Add((reader.GetString(0), reader.GetString(1)));
        }

        // Gate is released above; BuildSnippet reads files from disk and must
        // not run while the connection semaphore is held.
        return matches
            .Select(m => new SearchResult(m.Path, m.Name, BuildSnippet(m.Path, query)))
            .ToList();
    }

    private static string BuildSnippet(string filePath, string? query)
    {
        try
        {
            var terms = string.IsNullOrWhiteSpace(query)
                ? new List<string>()
                : ExtractSearchTerms(query!);
            string? firstBodyLine = null;
            string? bestLine = null;
            var bestScore = 0;
            // Skip a YAML frontmatter block when picking the fallback line —
            // otherwise filter-only searches show "---" as the snippet.
            var inFrontmatter = false;
            var sawFrontmatterStart = false;
            foreach (var line in File.ReadLines(filePath))
            {
                var trimmed = line.Trim();
                if (!sawFrontmatterStart && trimmed == "---")
                {
                    sawFrontmatterStart = true;
                    inFrontmatter = true;
                    continue;
                }
                if (inFrontmatter)
                {
                    if (trimmed == "---") inFrontmatter = false;
                    continue;
                }
                if (trimmed.Length == 0) continue;
                firstBodyLine ??= TrimSnippet(trimmed);

                if (terms.Count == 0) continue;
                var score = 0;
                foreach (var term in terms)
                    if (trimmed.Contains(term, StringComparison.OrdinalIgnoreCase))
                        score++;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestLine = TrimSnippet(trimmed);
                    if (score == terms.Count) return bestLine; // full match — done
                }
            }
            return bestLine ?? firstBodyLine ?? "";
        }
        catch { return ""; /* file deleted between match and read */ }
    }

    private static string TrimSnippet(string line) =>
        line.Length > 200 ? string.Concat(line.AsSpan(0, 200), "...") : line;

    // Tokenise an FTS5 query into the bare terms a snippet probe should look
    // for. Handles "phrase quotes", AND/OR/NOT/NEAR keywords, column:value
    // qualifiers (column name dropped, value kept), trailing wildcards, and
    // grouping/affinity punctuation. Best-effort — FTS5 grammar is bigger
    // than this, but covers the common cases.
    private static List<string> ExtractSearchTerms(string query)
    {
        var terms = new List<string>();
        var i = 0;
        while (i < query.Length)
        {
            var c = query[i];
            if (char.IsWhiteSpace(c) || c is '(' or ')' or '+' or '-' or '^' or ',')
            {
                i++;
                continue;
            }
            if (c == '"')
            {
                var end = query.IndexOf('"', i + 1);
                if (end < 0) break;
                var phrase = query.Substring(i + 1, end - i - 1).Trim();
                if (phrase.Length > 0) terms.Add(phrase);
                i = end + 1;
                continue;
            }
            var start = i;
            while (i < query.Length && (char.IsLetterOrDigit(query[i]) || query[i] is '_' or '*'))
                i++;
            if (start == i) { i++; continue; }
            // `column:` qualifier — drop the column name; the value is parsed next iteration.
            if (i < query.Length && query[i] == ':')
            {
                i++;
                continue;
            }
            var token = query.Substring(start, i - start);
            if (token is "AND" or "OR" or "NOT" or "NEAR") continue;
            while (token.EndsWith('*')) token = token[..^1];
            if (token.Length > 0) terms.Add(token);
        }
        return terms;
    }
}
