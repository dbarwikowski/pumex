namespace Pumex.Daemon;

/// <summary>
/// Fallback parser for any active extension without a dedicated
/// <see cref="IFormatParser"/>. Indexes the whole file as full-text body with no
/// properties, tags, or outgoing links — non-Markdown files are link targets,
/// never sources. Dedicated format parsers (CSV, JSON, …) shipped by later work
/// items supersede this for their extensions.
/// </summary>
public sealed class RawTextParser : IFormatParser
{
    public IReadOnlyCollection<string> Extensions => Array.Empty<string>();

    public NoteDocument Parse(string filePath)
    {
        var raw = File.ReadAllText(filePath);
        var normalized = raw.IndexOf('\r') >= 0 ? raw.Replace("\r\n", "\n") : raw;

        var info = new FileInfo(filePath);
        var mtime = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds();

        return new NoteDocument(
            Path: filePath,
            Frontmatter: new Dictionary<string, object>(),
            Tags: [],
            OutgoingLinks: [],
            Content: normalized,
            RawContent: normalized,
            Mtime: mtime,
            Size: info.Length);
    }
}
