using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Spectre.Console;

namespace Pumex.Cli;

/// <summary>
/// Renders CSV/TSV note bodies as a Spectre <see cref="Table"/>. Row 1 is treated
/// as the header. The delimiter (<c>,</c> or <c>\t</c>) is auto-detected from a
/// small sample; if detection fails the body is printed verbatim, matching the
/// raw passthrough used for any other non-Markdown format.
/// </summary>
internal static class CsvRenderer
{
    /// <summary>Number of leading non-empty lines sampled for delimiter detection.</summary>
    private const int SampleLines = 5;

    public static void Render(string text, int limit)
    {
        var delimiter = DetectDelimiter(text);
        if (delimiter is null)
        {
            AnsiConsole.WriteLine(text); // raw passthrough — same as the non-MD fallback
            return;
        }

        var (headers, rows) = Parse(text, delimiter.Value);
        if (headers.Count == 0)
        {
            AnsiConsole.WriteLine(text);
            return;
        }

        var table = new Table { Border = TableBorder.Rounded };
        foreach (var header in headers)
            table.AddColumn(new TableColumn(new Markup(header.EscapeMarkup())));

        var shown = limit < 0 ? rows.Count : Math.Min(limit, rows.Count);
        for (var r = 0; r < shown; r++)
        {
            var row = rows[r];
            var cells = new Markup[headers.Count];
            for (var c = 0; c < headers.Count; c++)
                cells[c] = new Markup(c < row.Count ? row[c].EscapeMarkup() : string.Empty);
            table.AddRow(cells);
        }

        AnsiConsole.Write(table);

        var notice = TruncationNotice(shown, rows.Count);
        if (notice is not null)
            AnsiConsole.MarkupLine($"[dim]{notice}[/]");
    }

    /// <summary>
    /// Detects the delimiter from the first <see cref="SampleLines"/> non-empty lines.
    /// A delimiter matches only if every sampled line contains the same count and that
    /// count is ≥ 1; <c>,</c> wins over <c>\t</c> when both match. Returns <c>null</c>
    /// when neither is consistent (e.g. prose) — the caller then falls back to raw text.
    /// </summary>
    internal static char? DetectDelimiter(string text)
    {
        var sample = new List<string>(SampleLines);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.EndsWith('\r') ? raw[..^1] : raw;
            if (line.Length == 0) continue;
            sample.Add(line);
            if (sample.Count == SampleLines) break;
        }
        if (sample.Count == 0) return null;

        if (HasUniformCount(sample, ',')) return ',';
        if (HasUniformCount(sample, '\t')) return '\t';
        return null;
    }

    private static bool HasUniformCount(List<string> lines, char delimiter)
    {
        var count = CountChar(lines[0], delimiter);
        if (count < 1) return false;
        for (var i = 1; i < lines.Count; i++)
            if (CountChar(lines[i], delimiter) != count) return false;
        return true;
    }

    private static int CountChar(string line, char c)
    {
        var n = 0;
        foreach (var ch in line)
            if (ch == c) n++;
        return n;
    }

    /// <summary>
    /// Parses the text into a header row plus data rows using the given delimiter.
    /// CsvHelper handles quoting; malformed rows are skipped rather than throwing so
    /// one bad line never blanks the whole table.
    /// </summary>
    internal static (List<string> Headers, List<List<string>> Rows) Parse(string text, char delimiter)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter.ToString(),
            HasHeaderRecord = false,
            DetectDelimiter = false,
            BadDataFound = null,
            MissingFieldFound = null,
        };

        using var reader = new StringReader(text);
        using var parser = new CsvParser(reader, config);

        var records = new List<List<string>>();
        while (parser.Read())
        {
            var record = parser.Record;
            if (record is not null)
                records.Add([.. record]);
        }

        if (records.Count == 0) return ([], []);
        var headers = records[0];
        var rows = records.GetRange(1, records.Count - 1);
        return (headers, rows);
    }

    /// <summary>"showing X of Y rows" when capped, otherwise <c>null</c>.</summary>
    internal static string? TruncationNotice(int shown, int total) =>
        shown < total ? $"showing {shown} of {total} rows" : null;
}
