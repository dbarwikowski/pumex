using Spectre.Console;

namespace Pumex.Cli;

/// <summary>
/// Dispatches `note read` rendering by file extension. Markdown gets the
/// <see cref="MarkdownRenderer"/>; CSV/TSV get the <see cref="CsvRenderer"/>;
/// everything else falls back to raw passthrough. Format-specific renderers
/// (JSON tree, …) are registered here by their own work items. The
/// <paramref name="limit"/> caps tabular row output and is ignored by renderers
/// that don't paginate.
/// </summary>
internal static class DocumentRenderer
{
    private static readonly Dictionary<string, Action<string, int>> Renderers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".md"] = (body, _) => MarkdownRenderer.Render(body),
            [".csv"] = CsvRenderer.Render,
            [".tsv"] = CsvRenderer.Render,
        };

    public static void Render(string path, string body, int limit)
    {
        var ext = Path.GetExtension(path);
        if (!string.IsNullOrEmpty(ext) && Renderers.TryGetValue(ext, out var render))
            render(body, limit);
        else
            AnsiConsole.WriteLine(body); // raw passthrough fallback
    }
}
