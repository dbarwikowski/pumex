using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Spectre.Console;
using Spectre.Console.Json;

namespace Pumex.Cli;

/// <summary>
/// Renders JSON note bodies with the Spectre <see cref="JsonText"/> widget
/// (syntax-highlighted). Parsing is lenient (JSONC: comments + trailing commas);
/// since the widget's own parser is strict, the body is first parsed leniently
/// and re-serialised to clean JSON. When the root is an array, <c>--limit</c>
/// caps how many top-level elements are shown; for object or scalar roots the
/// limit is ignored. Anything that fails to parse is printed verbatim, matching
/// the raw passthrough used for any other non-Markdown format.
/// </summary>
internal static class JsonRenderer
{
    private static readonly JsonDocumentOptions LenientOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        WriteIndented = true,
        // Keep Polish (and any non-ASCII) characters literal instead of \uXXXX,
        // consistent with InvariantGlobalization=false across the project.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void Render(string body, int limit)
    {
        if (!TryPrepare(body, limit, out var json, out var notice))
        {
            AnsiConsole.WriteLine(body); // raw passthrough — same as the non-MD fallback
            return;
        }

        AnsiConsole.Write(new JsonText(json));
        AnsiConsole.WriteLine();

        if (notice is not null)
            AnsiConsole.MarkupLine($"[dim]{notice}[/]");
    }

    /// <summary>
    /// Leniently parses <paramref name="body"/> and re-serialises it to strict,
    /// indented JSON for the widget. When the root is an array longer than
    /// <paramref name="limit"/>, only the first <paramref name="limit"/> elements
    /// are kept and <paramref name="notice"/> reports "showing X of Y elements".
    /// A negative limit means unlimited. Returns <c>false</c> when parsing fails,
    /// signalling the caller to fall back to raw passthrough.
    /// </summary>
    internal static bool TryPrepare(string body, int limit, out string json, out string? notice)
    {
        json = string.Empty;
        notice = null;

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(body, nodeOptions: null, documentOptions: LenientOptions);
        }
        catch (JsonException)
        {
            return false;
        }
        if (node is null) return false; // literal "null" — nothing to pretty-print

        if (node is JsonArray array && limit >= 0 && array.Count > limit)
        {
            var total = array.Count;
            var trimmed = new JsonArray();
            for (var i = 0; i < limit; i++)
                trimmed.Add(array[i]?.DeepClone()); // DeepClone: a JsonNode can't have two parents
            node = trimmed;
            notice = $"showing {limit} of {total} elements";
        }

        json = node.ToJsonString(OutputOptions);
        return true;
    }
}
