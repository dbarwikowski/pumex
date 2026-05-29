using Spectre.Console;

namespace Pumex.Cli;

/// <summary>
/// Dispatches `note read` rendering by file extension. Markdown gets the
/// <see cref="MarkdownRenderer"/>; everything else falls back to raw passthrough.
/// Format-specific renderers (CSV table, JSON tree, …) are registered here by
/// their own work items.
/// </summary>
internal static class DocumentRenderer
{
    private static readonly Dictionary<string, Action<string>> Renderers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".md"] = MarkdownRenderer.Render,
        };

    public static void Render(string path, string body)
    {
        var ext = Path.GetExtension(path);
        if (!string.IsNullOrEmpty(ext) && Renderers.TryGetValue(ext, out var render))
            render(body);
        else
            AnsiConsole.WriteLine(body); // raw passthrough fallback
    }
}
