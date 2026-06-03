using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Pumex.Daemon;

/// <summary>
/// Parses <c>.yaml</c> / <c>.yml</c> files. Like JSON, YAML earns a dedicated
/// parser because it lifts the top-level scalar keys of the root mapping into
/// queryable properties; the whole file is still the FTS body, so nested values
/// and keys stay searchable. Tags and outgoing links are empty — non-Markdown
/// files are link targets, never sources. In a multi-document file (<c>---</c>
/// separators) only the <b>first</b> document contributes properties; the whole
/// file is always indexed. Malformed YAML never crashes the daemon: it is
/// indexed as raw text with no properties. Independent of <see cref="NoteParser"/>
/// (which uses YamlDotNet for Markdown frontmatter) — they share the package, not
/// code, and have different semantics (frontmatter keeps nested values).
/// </summary>
public sealed class YamlFormatParser : IFormatParser
{
    private static readonly string[] HandledExtensions = [".yaml", ".yml"];
    public IReadOnlyCollection<string> Extensions => HandledExtensions;

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
    /// Top-level scalar keys (string / number / bool / etc.) of the first
    /// document's root mapping become properties, stored as their raw YAML scalar
    /// text (no type inference, so values stay culture-invariant). Nested mappings,
    /// sequences and YAML nulls are skipped — they reach search through the FTS
    /// body, not the flat property store. A non-mapping root (sequence or bare
    /// scalar) or malformed input yields no properties. Anchors/aliases are
    /// resolved by the representation model.
    /// </summary>
    internal static Dictionary<string, object> ExtractTopLevelScalars(string yaml)
    {
        var result = new Dictionary<string, object>();
        if (string.IsNullOrWhiteSpace(yaml))
            return result;

        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(yaml);
            stream.Load(reader);

            if (stream.Documents.Count == 0 ||
                stream.Documents[0].RootNode is not YamlMappingNode root)
                return result;

            foreach (var entry in root.Children)
            {
                if (entry.Key is not YamlScalarNode key || key.Value is null)
                    continue;
                if (entry.Value is YamlScalarNode value && !IsNull(value))
                    result[key.Value] = value.Value!;
                // Duplicate mapping keys are invalid YAML — YamlStream.Load throws
                // before we get here, so the file falls through to the raw fallback.
            }
        }
        catch (YamlException)
        {
            // Malformed YAML mustn't crash the daemon — indexed as raw body, no properties.
            return new Dictionary<string, object>();
        }
        return result;
    }

    /// <summary>
    /// A scalar counts as YAML null when it is an empty or a plain (unquoted)
    /// <c>~</c> / <c>null</c> literal. A quoted <c>"null"</c> is a real string and
    /// stays a property.
    /// </summary>
    private static bool IsNull(YamlScalarNode node)
    {
        if (node.Value is null) return true;
        if (node.Style is not (ScalarStyle.Plain or ScalarStyle.Any)) return false; // quoted → real string
        var v = node.Value;
        return v.Length == 0
            || v == "~"
            || string.Equals(v, "null", StringComparison.Ordinal)
            || string.Equals(v, "Null", StringComparison.Ordinal)
            || string.Equals(v, "NULL", StringComparison.Ordinal);
    }
}
