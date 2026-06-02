using System.Text.Json;

namespace Pumex.Daemon;

/// <summary>
/// Parses <c>.json</c> files. Unlike CSV (which is indexed fine by the raw-text
/// fallback), JSON earns a dedicated parser because it lifts top-level scalar
/// keys of a root object into queryable properties. The whole file is still the
/// FTS body, so nested values and keys stay searchable. Tags and outgoing links
/// are empty — non-Markdown files are link targets, never sources. Parsing is
/// lenient (JSONC: comments + trailing commas); a file that fails even that is
/// indexed as raw text with no properties, never crashing the daemon.
/// </summary>
public sealed class JsonFormatParser : IFormatParser
{
    private static readonly string[] HandledExtensions = [".json"];
    public IReadOnlyCollection<string> Extensions => HandledExtensions;

    private static readonly JsonDocumentOptions LenientOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public NoteDocument Parse(string filePath)
    {
        var raw = File.ReadAllText(filePath);
        // Replace always allocates; skip when there's no CR to replace.
        var normalized = raw.IndexOf('\r') >= 0 ? raw.Replace("\r\n", "\n") : raw;

        var info = new FileInfo(filePath);
        var mtime = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds();

        return new NoteDocument(
            Path: filePath,
            Frontmatter: ExtractTopLevelScalars(normalized),
            Tags: [],
            OutgoingLinks: [],
            Content: normalized,
            RawContent: normalized,
            Mtime: mtime,
            Size: info.Length);
    }

    /// <summary>
    /// Top-level scalar keys (string / number / bool) of a root JSON object become
    /// properties. Null values and nested objects/arrays are skipped — they reach
    /// search through the FTS body, not the flat property store. A non-object root
    /// (array or bare scalar) or malformed input yields no properties.
    /// </summary>
    internal static Dictionary<string, object> ExtractTopLevelScalars(string json)
    {
        var result = new Dictionary<string, object>();
        try
        {
            using var doc = JsonDocument.Parse(json, LenientOptions);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return result;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var value = ScalarText(prop.Value);
                if (value is not null)
                    result[prop.Name] = value; // last duplicate key wins (System.Text.Json default)
            }
        }
        catch (JsonException)
        {
            // Malformed JSON mustn't crash the daemon — indexed as raw body, no properties.
            return new Dictionary<string, object>();
        }
        return result;
    }

    /// <summary>
    /// Stringifies a scalar JSON value, or returns <c>null</c> for null / object /
    /// array. Numbers keep their raw token text and booleans use lowercase JSON
    /// form so values stay culture-invariant: with <c>InvariantGlobalization=false</c>
    /// a C# double would otherwise stringify with a locale decimal separator in the
    /// property store.
    /// </summary>
    private static string? ScalarText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null, // Null, Object, Array → not a top-level scalar property
    };
}
