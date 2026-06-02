namespace Pumex.Daemon;

/// <summary>
/// Dispatches a file to the right <see cref="IFormatParser"/> by extension,
/// falling back to <see cref="RawTextParser"/> for any extension without a
/// dedicated parser. Whether a given extension is actually indexed is decided
/// upstream by the per-vault <see cref="VaultIndexPolicy"/>; by the time a file
/// reaches here it has already passed that filter.
/// </summary>
public sealed class FormatParserRegistry
{
    private readonly Dictionary<string, IFormatParser> _byExtension =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly IFormatParser _fallback;

    public FormatParserRegistry(IEnumerable<IFormatParser> parsers, RawTextParser fallback)
    {
        _fallback = fallback;
        foreach (var parser in parsers)
            foreach (var ext in parser.Extensions)
                _byExtension[ext] = parser;
    }

    /// <summary>Convenience for tests: Markdown + JSON parsers + raw-text fallback.</summary>
    public static FormatParserRegistry Default() =>
        new([new NoteParser(), new JsonFormatParser()], new RawTextParser());

    public NoteDocument Parse(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        var parser = !string.IsNullOrEmpty(ext) && _byExtension.TryGetValue(ext, out var p)
            ? p
            : _fallback;
        return parser.Parse(filePath);
    }
}
