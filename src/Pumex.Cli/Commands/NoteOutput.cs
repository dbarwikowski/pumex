using Pumex.Contracts;
using Spectre.Console;

namespace Pumex.Cli.Commands;

/// <summary>Shared rendering of a <see cref="NoteContent"/> — the properties
/// table, tags line, and format-aware body. Used by <c>read</c> and
/// <c>task read</c> so they stay visually identical.</summary>
internal static class NoteOutput
{
    internal static void Render(NoteContent content, bool raw, int limit)
    {
        if (raw)
        {
            AnsiConsole.WriteLine(content.Raw);
            return;
        }

        if (content.Properties.Count > 0)
        {
            var table = new Table().Border(TableBorder.Minimal);
            table.AddColumn("Property");
            table.AddColumn("Value");
            foreach (var (k, v) in content.Properties)
                table.AddRow(k.EscapeMarkup(), v.EscapeMarkup());
            AnsiConsole.Write(table);
        }
        if (content.Tags.Count > 0)
            AnsiConsole.MarkupLine("[dim]tags:[/] " + string.Join(" ", content.Tags.Select(t => $"[blue]#{t.EscapeMarkup()}[/]")));
        AnsiConsole.WriteLine();
        DocumentRenderer.Render(content.Path, content.Body, limit);
    }

    /// <summary>Renders a note's checkboxes (the <c>read --tasks</c> view).
    /// Indices are the stable absolute numbers; <paramref name="pendingOnly"/>
    /// hides checked items without changing the numbering.</summary>
    internal static int RenderCheckboxes(IReadOnlyList<CheckboxItem> items, bool pendingOnly)
    {
        var shown = pendingOnly ? items.Where(i => !i.Checked).ToList() : items.ToList();
        if (shown.Count == 0)
        {
            AnsiConsole.MarkupLine(pendingOnly ? "[dim]no pending tasks[/]" : "[dim]no checkboxes[/]");
            return 0;
        }

        foreach (var item in shown)
        {
            var box = item.Checked ? "[green][[x]][/]" : "[grey][[ ]][/]";
            AnsiConsole.MarkupLine($"[dim]{item.Index,3}[/] {box} {item.Text.EscapeMarkup()}");
        }
        return 0;
    }
}
