using System.Text.RegularExpressions;
using Pumex.Contracts;

namespace Pumex.Daemon;

/// <summary>
/// Lexical context retrieval for AI agents. Reuses the FTS5 index (bm25
/// ranking) like <see cref="SearchRepository"/>, but returns multi-line
/// passages and drill-down pointers instead of one-line snippets. Retrieval
/// is keyword-only — there is no semantic/embedding layer.
/// </summary>
public partial class ContextRepository(IndexDbContext context) : IContextRepository
{
    // A section caps at this many lines so one giant note can't dominate the
    // pack; the per-pack char budget is enforced separately across sources.
    private const int MaxPassageLines = 15;

    public async Task<List<ContextResult>> ContextAsync(
        string query,
        int limit = 5,
        int budgetChars = 6000,
        long? vaultId = null)
    {
        var (fts, terms) = BuildContextQuery(query);
        // Nothing searchable (e.g. punctuation-only query) — no matches.
        if (string.IsNullOrEmpty(fts)) return [];

        var sql = new System.Text.StringBuilder("""
            SELECT n.path, n.name, n.format, v.path AS vault_path,
                   bm25(notes_fts) AS score,
                   (SELECT COUNT(*) FROM notes n2
                      WHERE n2.vault_id = n.vault_id
                        AND n2.name = n.name
                        AND n2.format = 'md') AS name_count
            FROM notes_fts
            JOIN notes  n ON n.id = notes_fts.rowid
            JOIN vaults v ON v.id = n.vault_id
            WHERE notes_fts MATCH @query
            """);
        var parameters = new List<(string, object)> { ("@query", fts) };

        if (vaultId is not null)
        {
            sql.Append(" AND n.vault_id = @vaultId");
            parameters.Add(("@vaultId", vaultId.Value));
        }

        // Rank ascending (most relevant first). Pull a few extra rows so that
        // sources whose passage turns out empty (file deleted mid-flight) don't
        // shrink the pack below `limit` unnecessarily.
        sql.Append(" ORDER BY rank LIMIT @limit");
        parameters.Add(("@limit", limit * 2));

        List<Row> rows;
        {
            using var _ = await context.AcquireAsync();
            rows = [];
            using var cmd = context.Command(sql.ToString(), parameters.ToArray());
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new Row(
                    Path: reader.GetString(0),
                    Name: reader.GetString(1),
                    Format: reader.GetString(2),
                    VaultPath: reader.GetString(3),
                    RawBm25: reader.GetDouble(4),
                    NameCount: reader.GetInt64(5)));
            }
        }

        // Gate released above: passage extraction reads files from disk and must
        // not run while the single-connection semaphore is held.
        var results = new List<ContextResult>();
        var usedChars = 0;
        foreach (var row in rows)
        {
            if (results.Count >= limit) break;

            var passage = ExtractPassage(ReadBodyLines(row.Path), terms, MaxPassageLines);
            if (passage.Length == 0) continue; // file vanished or empty body — skip

            // Budget governs passage text. Always keep the top source; drop
            // whole lower-ranked sources once the budget is exhausted.
            if (results.Count > 0 && usedChars + passage.Length > budgetChars) break;
            usedChars += passage.Length;

            var relPath = ToRelative(row.VaultPath, row.Path);
            var pointer = row.Format == "md" && row.NameCount == 1 ? row.Name : relPath;
            var score = Math.Round(-row.RawBm25, 1); // bm25 is negative; flip so higher = better

            results.Add(new ContextResult(relPath, passage, pointer, score, row.Format));
        }

        return results;
    }

    private readonly record struct Row(
        string Path, string Name, string Format, string VaultPath, double RawBm25, long NameCount);

    private static string ToRelative(string vaultPath, string notePath)
    {
        var rel = System.IO.Path.GetRelativePath(vaultPath, notePath);
        return rel.Replace('\\', '/');
    }

    // ── query building ──────────────────────────────────────────────────────

    // Stopwords stripped from natural-language queries so bm25 ranks on the
    // content words. Includes common question words ("how", "does", ...) since
    // agents tend to paste whole questions.
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "as", "at", "be", "but", "by", "can", "do", "does",
        "for", "from", "how", "i", "in", "into", "is", "it", "its", "of", "on", "or",
        "should", "so", "than", "that", "the", "their", "them", "then", "there",
        "these", "they", "this", "to", "was", "what", "when", "where", "which",
        "who", "why", "will", "with", "would", "you", "your",
    };

    /// <summary>
    /// Turns free text into an FTS5 OR query plus the bare terms a passage probe
    /// should look for. Stopwords are dropped; each surviving token is phrase-
    /// quoted (neutralising FTS operators) and OR-joined so bm25 floats notes
    /// matching more terms to the top. If every token is a stopword, the words
    /// are kept rather than producing an empty query.
    /// </summary>
    internal static (string Fts, List<string> Terms) BuildContextQuery(string text)
    {
        var tokens = TokenRegex().Matches(text ?? "").Select(m => m.Value).ToList();
        if (tokens.Count == 0) return ("", []);

        var kept = tokens.Where(t => !StopWords.Contains(t)).ToList();
        if (kept.Count == 0) kept = tokens; // all-stopword query — keep them

        var fts = string.Join(" OR ", kept.Select(t => "\"" + t.Replace("\"", "\"\"") + "\""));
        return (fts, kept);
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex TokenRegex();

    // ── passage extraction ──────────────────────────────────────────────────

    /// <summary>
    /// Reads a file and returns its body lines with any leading YAML frontmatter
    /// block stripped. Returns an empty list if the file can't be read.
    /// </summary>
    internal static List<string> ReadBodyLines(string filePath)
    {
        try
        {
            var lines = new List<string>();
            var sawFrontmatterStart = false;
            var inFrontmatter = false;
            foreach (var line in File.ReadLines(filePath))
            {
                if (!sawFrontmatterStart && line.Trim() == "---")
                {
                    sawFrontmatterStart = true;
                    inFrontmatter = true;
                    continue;
                }
                if (inFrontmatter)
                {
                    if (line.Trim() == "---") inFrontmatter = false;
                    continue;
                }
                lines.Add(line);
            }
            return lines;
        }
        catch { return []; /* file deleted between match and read */ }
    }

    /// <summary>
    /// Picks the best-matching section of a note's body: the paragraph
    /// containing the line with the most query terms, prefixed with the nearest
    /// Markdown heading above it (heading marks stripped). Caps at
    /// <paramref name="maxLines"/>. With no term hits, falls back to the first
    /// paragraph. Pure — no disk access, so it is unit-tested directly.
    /// </summary>
    internal static string ExtractPassage(IReadOnlyList<string> bodyLines, IReadOnlyList<string> terms, int maxLines)
    {
        // Index of the first non-blank line, used as the no-match fallback.
        var firstContent = -1;
        var bestIdx = -1;
        var bestScore = 0;
        for (var i = 0; i < bodyLines.Count; i++)
        {
            if (bodyLines[i].Trim().Length == 0) continue;
            if (firstContent < 0) firstContent = i;

            var score = 0;
            foreach (var term in terms)
                if (bodyLines[i].Contains(term, StringComparison.OrdinalIgnoreCase))
                    score++;
            if (score > bestScore)
            {
                bestScore = score;
                bestIdx = i;
            }
        }

        if (bestIdx < 0) bestIdx = firstContent;
        if (bestIdx < 0) return ""; // no content at all

        // Paragraph = contiguous non-blank lines around the best line.
        var pStart = bestIdx;
        var pEnd = bestIdx;
        while (pStart > 0 && bodyLines[pStart - 1].Trim().Length > 0) pStart--;
        while (pEnd < bodyLines.Count - 1 && bodyLines[pEnd + 1].Trim().Length > 0) pEnd++;

        var passage = new List<string>();

        // Prepend the nearest heading above the paragraph, if any.
        for (var h = pStart - 1; h >= 0; h--)
        {
            if (bodyLines[h].Trim().Length == 0) continue;
            if (IsHeading(bodyLines[h])) passage.Add(StripHeadingMarks(bodyLines[h]));
            break; // only look at the first non-blank line above the paragraph
        }

        for (var i = pStart; i <= pEnd && passage.Count < maxLines; i++)
            passage.Add(bodyLines[i].TrimEnd());

        return string.Join('\n', passage).Trim();
    }

    private static bool IsHeading(string line)
    {
        var t = line.TrimStart();
        var hashes = 0;
        while (hashes < t.Length && t[hashes] == '#') hashes++;
        return hashes is >= 1 and <= 6 && (hashes == t.Length || t[hashes] == ' ');
    }

    private static string StripHeadingMarks(string line) => line.TrimStart().TrimStart('#').Trim();
}
