using System.Text;
using Spectre.Console;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Pumex.Cli;

/// <summary>
/// Renders YAML note bodies with hand-rolled, line-based syntax highlighting.
/// Unlike JSON (which has a Spectre widget), YAML has none, and converting it to
/// JSON would destroy comments, anchors and key order — so the file is shown
/// verbatim with best-effort coloring of keys, sequence markers and comments.
/// For a single-document file whose root is a block sequence, <c>--limit</c> caps
/// how many top-level elements are shown (default 100); it is ignored for
/// mapping/scalar roots and multi-document files. Anything that fails to parse is
/// still printed (best-effort highlighting), never an error.
/// </summary>
internal static class YamlRenderer
{
    public static void Render(string body, int limit)
    {
        var (lines, notice) = Prepare(body, limit);
        foreach (var line in lines)
            AnsiConsole.MarkupLine(HighlightLine(line));
        if (notice is not null)
            AnsiConsole.MarkupLine($"[dim]{notice}[/]");
    }

    /// <summary>
    /// Splits <paramref name="body"/> into display lines and, for a single-document
    /// block-sequence root longer than <paramref name="limit"/>, keeps only the
    /// first <paramref name="limit"/> top-level elements (with their nested lines)
    /// and reports "showing X of Y elements". A negative limit means unlimited.
    /// Mapping/scalar roots, multi-document files, flow sequences and unparseable
    /// input are returned whole with no notice.
    /// </summary>
    internal static (List<string> Lines, string? Notice) Prepare(string body, int limit)
    {
        var lines = SplitLines(body);
        if (limit < 0 || !IsSequenceRoot(body))
            return (lines, null);

        var total = lines.Count(IsTopLevelSequenceItem);
        if (total <= limit)
            return (lines, null);

        var kept = new List<string>();
        var shown = 0;
        foreach (var line in lines)
        {
            if (IsTopLevelSequenceItem(line))
            {
                if (shown >= limit) break;
                shown++;
            }
            kept.Add(line);
        }
        return (kept, $"showing {limit} of {total} elements");
    }

    /// <summary>True when the body is a single document whose root is a sequence.</summary>
    private static bool IsSequenceRoot(string body)
    {
        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(body);
            stream.Load(reader);
            return stream.Documents.Count == 1 &&
                   stream.Documents[0].RootNode is YamlSequenceNode;
        }
        catch (YamlException)
        {
            return false;
        }
    }

    private static bool IsTopLevelSequenceItem(string line) =>
        line.StartsWith("- ", StringComparison.Ordinal) || line == "-";

    private static List<string> SplitLines(string body)
    {
        var normalized = body.IndexOf('\r') >= 0 ? body.Replace("\r\n", "\n") : body;
        var lines = normalized.Split('\n').ToList();
        // Drop the single empty element produced by a trailing newline so we don't
        // print an extra blank line versus the source file.
        if (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    /// <summary>
    /// Best-effort, line-based highlighting. Returns Spectre markup; all literal
    /// content is escaped so YAML flow syntax (<c>[</c>, <c>]</c>) is never parsed
    /// as a markup tag. Trailing comments are intentionally not split out (a bare
    /// <c>#</c> can appear inside URLs/strings); only whole-line comments are dimmed.
    /// </summary>
    internal static string HighlightLine(string line)
    {
        var trimmedStart = line.TrimStart();
        var trimmed = line.Trim();

        if (trimmedStart.StartsWith('#'))
            return $"[grey]{Markup.Escape(line)}[/]";
        if (trimmed is "---" or "...")
            return $"[grey]{Markup.Escape(line)}[/]";

        var indent = line.Length - trimmedStart.Length;
        var prefix = line[..indent]; // leading spaces only — no markup needed
        var rest = trimmedStart;

        // Leading sequence markers ("- ", possibly nested "- - ").
        var marker = new StringBuilder();
        while (rest.StartsWith("- ", StringComparison.Ordinal))
        {
            marker.Append("- ");
            rest = rest[2..];
        }
        if (rest == "-")
        {
            marker.Append('-');
            rest = "";
        }

        var markerMarkup = marker.Length > 0 ? $"[yellow]{Markup.Escape(marker.ToString())}[/]" : "";
        return prefix + markerMarkup + HighlightKeyValue(rest);
    }

    private static string HighlightKeyValue(string rest)
    {
        if (rest.Length == 0)
            return "";

        var sep = FindKeySeparator(rest);
        if (sep < 0)
            return Markup.Escape(rest); // plain scalar / block continuation

        var key = rest[..sep];
        var value = rest.Length > sep + 1 ? rest[(sep + 1)..] : "";
        return $"[cyan]{Markup.Escape(key)}[/][grey]:[/]{Markup.Escape(value)}";
    }

    /// <summary>
    /// Index of the colon separating a mapping key from its value: the first
    /// "<c>: </c>", else a trailing "<c>:</c>". Best-effort only (cosmetic) — a
    /// colon inside a quoted value may be mis-split, which only affects coloring.
    /// </summary>
    private static int FindKeySeparator(string s)
    {
        var idx = s.IndexOf(": ", StringComparison.Ordinal);
        if (idx >= 0)
            return idx;
        return s.EndsWith(':') ? s.Length - 1 : -1;
    }
}
