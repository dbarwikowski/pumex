using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Pumex.Contracts;

namespace Pumex.Daemon.Ipc;

/// <summary>
/// Reads and toggles GFM task-list checkboxes (<c>- [ ]</c> / <c>- [x]</c>) in a
/// Markdown note. Parsing goes through Markdig's task-list extension so only real
/// task items count — bracket text inside fenced code blocks is ignored.
///
/// Indices are 1-based and assigned in document order over <em>all</em> checkbox
/// items, so a checkbox's number is stable regardless of any pending filter.
/// </summary>
internal static partial class CheckboxScanner
{
    // PreciseSourceLocation makes inline Line/Span accurate, which the toggle
    // relies on to find the physical line to rewrite.
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder { PreciseSourceLocation = true }.UseTaskLists().Build();

    private readonly record struct Scanned(int Index, bool Checked, string Text, int Line);

    public static List<CheckboxItem> Items(string markdown) =>
        Scan(Normalize(markdown), out _)
            .Select(s => new CheckboxItem(s.Index, s.Checked, s.Text))
            .ToList();

    public static (string Content, CheckboxItem Item) Toggle(string markdown, int index)
    {
        var normalized = Normalize(markdown);
        var scanned = Scan(normalized, out var lines);

        var target = scanned.FirstOrDefault(s => s.Index == index);
        if (target.Index == 0)
            throw new ArgumentOutOfRangeException(nameof(index),
                $"No checkbox #{index}; note has {scanned.Count} checkbox(es).");

        lines[target.Line] = Flip(lines[target.Line], target.Checked);
        var content = string.Join('\n', lines);
        return (content, new CheckboxItem(target.Index, !target.Checked, target.Text));
    }

    private static List<Scanned> Scan(string normalized, out string[] lines)
    {
        lines = normalized.Split('\n');
        var doc = Markdown.Parse(normalized, Pipeline);

        var result = new List<Scanned>();
        var index = 0;
        foreach (var task in doc.Descendants<TaskList>())
        {
            index++;
            result.Add(new Scanned(index, task.Checked, ExtractText(lines, task.Line), task.Line));
        }
        return result;
    }

    // The marker text lives on the same source line as the checkbox; strip the
    // list bullet + checkbox prefix and keep the remainder.
    private static string ExtractText(string[] lines, int line)
    {
        if (line < 0 || line >= lines.Length) return "";
        return MarkerPrefix().Replace(lines[line], "").Trim();
    }

    private static string Flip(string line, bool currentlyChecked)
    {
        if (currentlyChecked)
        {
            var i = line.IndexOf("[x]", StringComparison.OrdinalIgnoreCase);
            return i < 0 ? line : line[..i] + "[ ]" + line[(i + 3)..];
        }

        var j = line.IndexOf("[ ]", StringComparison.Ordinal);
        return j < 0 ? line : line[..j] + "[x]" + line[(j + 3)..];
    }

    private static string Normalize(string raw) =>
        raw.IndexOf('\r') >= 0
            ? raw.Replace("\r\n", "\n").Replace('\r', '\n')
            : raw;

    [GeneratedRegex(@"^\s*[-*+]\s+\[[ xX]\]\s?")]
    private static partial Regex MarkerPrefix();
}
